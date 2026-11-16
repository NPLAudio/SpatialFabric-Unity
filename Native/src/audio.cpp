// Original engine-independent backend; MIT. No Unreal or Unity headers.
#define SF_AUDIO_BUILD
#include "spatialfabric_audio.h"
#include "pcm.h"
#include "live.h"
#include <atomic>
#include <chrono>
#include <mutex>
#include <future>
#include <sstream>
#include <string>
#include <thread>
#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#include <audioclient.h>
#include <mmdeviceapi.h>
#include <mmreg.h>
#include <wrl/client.h>
#include <initguid.h>
#include <functiondiscoverykeys_devpkey.h>
using Microsoft::WRL::ComPtr;
#elif defined(__APPLE__)
#include <AudioToolbox/AudioToolbox.h>
#include <CoreAudio/CoreAudio.h>
#include <CoreFoundation/CoreFoundation.h>
#include <unistd.h>
#endif
struct sf_buffer { std::shared_ptr<const sf::Pcm> pcm; };
struct sf_player {
    std::mutex control, status_lock;
    std::thread worker;
    std::atomic<bool> stop{false}, loop{false};
    sf_status status{};
#ifdef _WIN32
    HANDLE stop_event=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    ~sf_player() { if(stop_event) CloseHandle(stop_event); }
#endif
};
struct sf_live {
    std::mutex control, status_lock;
    std::thread worker;
    std::atomic<bool> stop{false};
    std::atomic<float> gain{1.f};
    sf_live_status status{};
#ifdef _WIN32
    HANDLE stop_event=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    ~sf_live() { if(stop_event) CloseHandle(stop_event); }
#endif
};
namespace {
void text(char* output,int capacity,const std::string& value) {
    if(!output || capacity<1) return;
    size_t n=std::min(value.size(),size_t(capacity-1));
    // Keep UTF-8 truncation at a code-point boundary.
    if(n<value.size()) while(n>0 && (uint8_t(value[n])&0xc0)==0x80) --n;
    std::memcpy(output,value.data(),n); output[n]=0;
}
std::string json_string(const std::string& s) {
    std::string out="\""; const char* hex="0123456789abcdef";
    for(unsigned char c:s) { if(c=='"' || c=='\\') {out+='\\';out+=char(c);} else if(c<32) {out+="\\u00";out+=hex[c>>4];out+=hex[c&15];} else out+=char(c); }
    return out+"\"";
}
void cancel_join(sf_player* p) {
    p->stop=true;
#ifdef _WIN32
    if(p->stop_event) SetEvent(p->stop_event);
#endif
    if(p->worker.joinable()) p->worker.join();
}
void cancel_join(sf_live* p) {
    p->stop=true;
#ifdef _WIN32
    if(p->stop_event) SetEvent(p->stop_event);
#endif
    if(p->worker.joinable()) p->worker.join();
}
#ifdef _WIN32
void check(HRESULT hr,const char* context) { if(FAILED(hr)) {std::ostringstream s;s<<context<<" (HRESULT 0x"<<std::hex<<uint32_t(hr)<<")";throw std::runtime_error(s.str());} }
struct ComScope { ComScope(){check(CoInitializeEx(nullptr,COINIT_MULTITHREADED),"COM initialization");} ~ComScope(){CoUninitialize();} };
std::wstring wide(const char* s) {
    if(!s || !*s) return {};
    int n=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,s,-1,nullptr,0); if(n<1 || n>32768) throw std::runtime_error("Invalid UTF-8 device ID");
    std::wstring out(size_t(n),0); MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,s,-1,out.data(),n); out.resize(size_t(n-1)); return out;
}
std::string utf8(const wchar_t* s) {
    if(!s) return {}; int n=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,s,-1,nullptr,0,nullptr,nullptr); if(n<1) throw std::runtime_error("Invalid device Unicode");
    std::string out(size_t(n),0);WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,s,-1,out.data(),n,nullptr,nullptr);out.resize(size_t(n-1));return out;
}
struct Format { WAVEFORMATEX* value=nullptr; ~Format(){CoTaskMemFree(value);} };
struct DeviceId { LPWSTR value=nullptr; ~DeviceId(){CoTaskMemFree(value);} };
struct Event { HANDLE value=CreateEventW(nullptr,FALSE,FALSE,nullptr); ~Event(){if(value)CloseHandle(value);} };
struct StreamStop { IAudioClient* client; bool running=false; ~StreamStop(){if(running)client->Stop();} };
struct Cancelled {};
GUID subtype(WORD type) { return GUID{type,0,0x0010,{0x80,0,0,0xaa,0,0x38,0x9b,0x71}}; }
void format_info(WAVEFORMATEX* f,bool& floating,int& bits,int& valid) {
    if(!f || f->nChannels<1 || f->nChannels>64 || f->nSamplesPerSec<8000 || f->nSamplesPerSec>384000) throw std::runtime_error("Invalid device format");
    bits=f->wBitsPerSample;valid=bits; WORD tag=f->wFormatTag;
    if(tag==WAVE_FORMAT_EXTENSIBLE) {
        if(f->cbSize<22) throw std::runtime_error("Truncated extensible device format");
        auto ext=reinterpret_cast<WAVEFORMATEXTENSIBLE*>(f);valid=ext->Samples.wValidBitsPerSample?ext->Samples.wValidBitsPerSample:bits;
        if(IsEqualGUID(ext->SubFormat,subtype(WAVE_FORMAT_IEEE_FLOAT))) tag=WAVE_FORMAT_IEEE_FLOAT;
        else if(IsEqualGUID(ext->SubFormat,subtype(WAVE_FORMAT_PCM))) tag=WAVE_FORMAT_PCM;
        else throw std::runtime_error("Unsupported device PCM subtype");
    }
    floating=tag==WAVE_FORMAT_IEEE_FLOAT;
    if((floating?bits!=32:(tag!=WAVE_FORMAT_PCM || (bits!=16 && bits!=24 && bits!=32))) || valid<1 || valid>bits || f->nBlockAlign!=f->nChannels*(bits/8)) throw std::runtime_error("Unsupported device sample format");
}
std::string devices(bool capture=false) {
    const EDataFlow flow=capture?eCapture:eRender;
    ComScope com;ComPtr<IMMDeviceEnumerator> enumerator;check(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&enumerator)),"Create device enumerator");
    ComPtr<IMMDevice> def;std::string default_id;if(SUCCEEDED(enumerator->GetDefaultAudioEndpoint(flow,eConsole,&def))) {DeviceId id;check(def->GetId(&id.value),"Default device ID");default_id=utf8(id.value);}
    ComPtr<IMMDeviceCollection> collection;check(enumerator->EnumAudioEndpoints(flow,DEVICE_STATE_ACTIVE,&collection),"Enumerate audio devices");UINT count=0;check(collection->GetCount(&count),"Count devices");
    if(count>256)throw std::runtime_error("Device count limit exceeded");
    std::string out="{\"devices\":[";
    for(UINT i=0;i<count;++i) {
        ComPtr<IMMDevice> device;check(collection->Item(i,&device),"Get device");DeviceId id;check(device->GetId(&id.value),"Device ID");std::string ident=utf8(id.value),name=ident;
        ComPtr<IPropertyStore> props;if(SUCCEEDED(device->OpenPropertyStore(STGM_READ,&props))) {PROPVARIANT v;PropVariantInit(&v);if(SUCCEEDED(props->GetValue(PKEY_Device_FriendlyName,&v)) && v.vt==VT_LPWSTR)name=utf8(v.pwszVal);PropVariantClear(&v);}
        int channels=0,rate=0;ComPtr<IAudioClient> client;if(SUCCEEDED(device->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(client.GetAddressOf())))) {Format f;if(SUCCEEDED(client->GetMixFormat(&f.value))) {channels=f.value->nChannels;rate=int(f.value->nSamplesPerSec);}}
        if(i)out+=',';out+="{\"id\":"+json_string(ident)+",\"name\":"+json_string(name)+",\"channels\":"+std::to_string(channels)+",\"sample_rate\":"+std::to_string(rate)+",\"is_default\":"+(ident==default_id?"true":"false")+"}";
    }
    return out+"]}";
}
int run_device(sf_player* p,std::shared_ptr<const sf::Pcm> pcm,const std::string& id,int selected,bool exclusive,int buffer_ms,bool& started) {
    ComScope com;ComPtr<IMMDeviceEnumerator> enumerator;check(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&enumerator)),"Create device enumerator");
    ComPtr<IMMDevice> device;if(id.empty())check(enumerator->GetDefaultAudioEndpoint(eRender,eConsole,&device),"Get default output");else {auto w=wide(id.c_str());check(enumerator->GetDevice(w.c_str(),&device),"Get configured output (no default fallback)");}
    ComPtr<IAudioClient> client;check(device->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(client.GetAddressOf())),"Activate output client");
    Format mix;check(client->GetMixFormat(&mix.value),"Get output mix format");bool floating=false;int bits=0,valid=0;format_info(mix.value,floating,bits,valid);
    if(selected<0 || selected>mix.value->nChannels)throw std::runtime_error("Selected output channel exceeds actual device channels");
    AUDCLNT_SHAREMODE mode=exclusive?AUDCLNT_SHAREMODE_EXCLUSIVE:AUDCLNT_SHAREMODE_SHARED;
    if(exclusive)check(client->IsFormatSupported(mode,mix.value,nullptr),"Exclusive mix format unsupported (no silent shared fallback)");
    REFERENCE_TIME period=0,minimum=0;check(client->GetDevicePeriod(&period,&minimum),"Get device period");
    REFERENCE_TIME duration=buffer_ms>0?REFERENCE_TIME(buffer_ms)*10000:(exclusive?std::max(period,minimum):0);
    DWORD flags=AUDCLNT_STREAMFLAGS_EVENTCALLBACK|(exclusive?0:AUDCLNT_STREAMFLAGS_NOPERSIST);
    HRESULT init=client->Initialize(mode,flags,duration,exclusive?duration:0,mix.value,nullptr);
    if(exclusive && init==AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED) {
        UINT32 aligned=0;check(client->GetBufferSize(&aligned),"Get exclusive aligned buffer size");
        duration=REFERENCE_TIME(std::llround(10000000.0*aligned/mix.value->nSamplesPerSec));client.Reset();
        check(device->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(client.GetAddressOf())),"Reactivate aligned output");
        init=client->Initialize(mode,flags,duration,duration,mix.value,nullptr);
    }
    check(init,"Initialize WASAPI stream");Event audio;if(!audio.value)throw std::runtime_error("Create WASAPI event failed");check(client->SetEventHandle(audio.value),"Set audio event");
    UINT32 frames=0;check(client->GetBufferSize(&frames),"Get device buffer size");
    if(frames<1 || frames>1024*1024)throw std::runtime_error("Device buffer size limit exceeded");
    ComPtr<IAudioRenderClient> render;check(client->GetService(IID_PPV_ARGS(&render)),"Get render client");StreamStop stop{client.Get()};
    std::vector<float> scratch(size_t(frames)*mix.value->nChannels);double position=0;bool eof=false;
    auto fill=[&](UINT32 count) {
        BYTE* bytes=nullptr;check(render->GetBuffer(count,&bytes),"Get output buffer");
        try {eof=sf::render(*pcm,position,scratch.data(),int(count),mix.value->nChannels,int(mix.value->nSamplesPerSec),selected,p->loop.load());sf::encode(scratch.data(),int(count*mix.value->nChannels),bytes,floating,bits,valid);}
        catch(...) {render->ReleaseBuffer(count,AUDCLNT_BUFFERFLAGS_SILENT);throw;}
        check(render->ReleaseBuffer(count,0),"Submit output buffer");std::lock_guard<std::mutex> lock(p->status_lock);p->status.rendered_frames+=count;
    };
    if(p->stop)throw Cancelled{};fill(frames);if(p->stop)throw Cancelled{};
    ResetEvent(audio.value); // Do not mistake a pre-start ready signal for an already-consumed exclusive buffer.
    check(client->Start(),"Start WASAPI stream");stop.running=true;started=true;
    {std::lock_guard<std::mutex> lock(p->status_lock);p->status.state=SF_PLAYING;++p->status.started_sequence;p->status.output_channels=mix.value->nChannels;p->status.output_rate=int(mix.value->nSamplesPerSec);p->status.exclusive_active=exclusive?1:0;p->status.buffer_frames=int(frames);}
    HANDLE events[]={p->stop_event,audio.value};
    for(;;) {
        DWORD wait=WaitForMultipleObjects(2,events,FALSE,2000);
        if(wait==WAIT_OBJECT_0 || p->stop)throw Cancelled{};
        if(wait!=WAIT_OBJECT_0+1)throw std::runtime_error("WASAPI event wait failed/timed out; device may be unavailable");
        if(eof && p->loop) eof=false;
        if(eof) {
            {std::lock_guard<std::mutex> lock(p->status_lock);p->status.state=SF_DRAINING;}
            if(exclusive)break;
            UINT32 padding=0;check(client->GetCurrentPadding(&padding),"Drain output padding");if(padding==0)break;continue;
        }
        UINT32 available=frames;
        if(!exclusive) {UINT32 padding=0;check(client->GetCurrentPadding(&padding),"Get output padding");if(padding>frames)throw std::runtime_error("Invalid device buffer padding");available=frames-padding;}
        if(available)fill(available);
    }
    return SF_NATURAL_END;
}
std::string access_hint(const std::string& what) {
    return what+" (if this is an input device, check Windows Settings > Privacy > Microphone for desktop apps)";
}
int run_live(sf_live* p,const std::string& in_id,int in_sel,const std::string& out_id,int out_sel,bool exclusive,int buffer_ms,bool& started) {
    ComScope com;ComPtr<IMMDeviceEnumerator> enumerator;check(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&enumerator)),"Create device enumerator");
    ComPtr<IMMDevice> in_dev,out_dev;
    if(in_id.empty())check(enumerator->GetDefaultAudioEndpoint(eCapture,eConsole,&in_dev),"Get default input");else {auto w=wide(in_id.c_str());check(enumerator->GetDevice(w.c_str(),&in_dev),"Get configured input (no default fallback)");}
    if(out_id.empty())check(enumerator->GetDefaultAudioEndpoint(eRender,eConsole,&out_dev),"Get default output");else {auto w=wide(out_id.c_str());check(enumerator->GetDevice(w.c_str(),&out_dev),"Get configured output (no default fallback)");}
    ComPtr<IAudioClient> in_client,out_client;
    try {check(in_dev->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(in_client.GetAddressOf())),"Activate input client");}catch(const std::exception& e){throw std::runtime_error(access_hint(e.what()));}
    check(out_dev->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(out_client.GetAddressOf())),"Activate output client");
    Format in_mix,out_mix;check(in_client->GetMixFormat(&in_mix.value),"Get input mix format");check(out_client->GetMixFormat(&out_mix.value),"Get output mix format");
    bool in_float=false,out_float=false;int in_bits=0,in_valid=0,out_bits=0,out_valid=0;format_info(in_mix.value,in_float,in_bits,in_valid);format_info(out_mix.value,out_float,out_bits,out_valid);
    if(in_sel<0 || in_sel>in_mix.value->nChannels)throw std::runtime_error("Selected input channel exceeds actual device channels");
    if(out_sel<0 || out_sel>out_mix.value->nChannels)throw std::runtime_error("Selected output channel exceeds actual device channels");
    // Capture: always shared and event driven.
    {HRESULT init=in_client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_EVENTCALLBACK|AUDCLNT_STREAMFLAGS_NOPERSIST,buffer_ms>0?REFERENCE_TIME(buffer_ms)*10000:0,0,in_mix.value,nullptr);
     if(FAILED(init))throw std::runtime_error(access_hint(std::string("Initialize WASAPI capture stream (HRESULT 0x")+[&]{std::ostringstream s;s<<std::hex<<uint32_t(init);return s.str();}()+")"));}
    // Render: same exclusive/shared policy as sf_player_start.
    AUDCLNT_SHAREMODE mode=exclusive?AUDCLNT_SHAREMODE_EXCLUSIVE:AUDCLNT_SHAREMODE_SHARED;
    if(exclusive)check(out_client->IsFormatSupported(mode,out_mix.value,nullptr),"Exclusive mix format unsupported (no silent shared fallback)");
    REFERENCE_TIME period=0,minimum=0;check(out_client->GetDevicePeriod(&period,&minimum),"Get device period");
    REFERENCE_TIME duration=buffer_ms>0?REFERENCE_TIME(buffer_ms)*10000:(exclusive?std::max(period,minimum):0);
    DWORD flags=AUDCLNT_STREAMFLAGS_EVENTCALLBACK|(exclusive?0:AUDCLNT_STREAMFLAGS_NOPERSIST);
    HRESULT init=out_client->Initialize(mode,flags,duration,exclusive?duration:0,out_mix.value,nullptr);
    if(exclusive && init==AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED) {
        UINT32 aligned=0;check(out_client->GetBufferSize(&aligned),"Get exclusive aligned buffer size");
        duration=REFERENCE_TIME(std::llround(10000000.0*aligned/out_mix.value->nSamplesPerSec));out_client.Reset();
        check(out_dev->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(out_client.GetAddressOf())),"Reactivate aligned output");
        init=out_client->Initialize(mode,flags,duration,duration,out_mix.value,nullptr);
    }
    check(init,"Initialize WASAPI output stream");
    Event in_event,out_event;if(!in_event.value || !out_event.value)throw std::runtime_error("Create WASAPI events failed");
    check(in_client->SetEventHandle(in_event.value),"Set input event");check(out_client->SetEventHandle(out_event.value),"Set output event");
    UINT32 in_frames=0,out_frames=0;check(in_client->GetBufferSize(&in_frames),"Get input buffer size");check(out_client->GetBufferSize(&out_frames),"Get output buffer size");
    if(in_frames<1 || in_frames>1024*1024 || out_frames<1 || out_frames>1024*1024)throw std::runtime_error("Device buffer size limit exceeded");
    ComPtr<IAudioCaptureClient> capture;check(in_client->GetService(IID_PPV_ARGS(&capture)),"Get capture client");
    ComPtr<IAudioRenderClient> render;check(out_client->GetService(IID_PPV_ARGS(&render)),"Get render client");
    const int in_ch=in_mix.value->nChannels,out_ch=out_mix.value->nChannels,in_rate=int(in_mix.value->nSamplesPerSec),out_rate=int(out_mix.value->nSamplesPerSec);
    sf::LiveBridge bridge(in_ch,in_rate,out_ch,out_rate,in_sel,out_sel,std::clamp(buffer_ms>0?buffer_ms*2:40,20,500));
    std::vector<float> capture_scratch(size_t(in_frames)*in_ch*2),render_scratch(size_t(out_frames)*out_ch);
    auto submit=[&](UINT32 count) {
        BYTE* bytes=nullptr;check(render->GetBuffer(count,&bytes),"Get output buffer");
        try {bridge.pull(render_scratch.data(),int(count),p->gain.load());sf::encode(render_scratch.data(),int(count*out_ch),bytes,out_float,out_bits,out_valid);}
        catch(...) {render->ReleaseBuffer(count,AUDCLNT_BUFFERFLAGS_SILENT);throw;}
        check(render->ReleaseBuffer(count,0),"Submit output buffer");
    };
    auto drain_capture=[&] {
        for(;;) {
            UINT32 packet=0;check(capture->GetNextPacketSize(&packet),"Get capture packet size");if(!packet)break;
            BYTE* data=nullptr;UINT32 n=0;DWORD packet_flags=0;check(capture->GetBuffer(&data,&n,&packet_flags,nullptr,nullptr),"Get capture buffer");
            try {
                if(size_t(n)*in_ch>capture_scratch.size())capture_scratch.resize(size_t(n)*in_ch);
                if(packet_flags&AUDCLNT_BUFFERFLAGS_SILENT)std::fill(capture_scratch.begin(),capture_scratch.begin()+size_t(n)*in_ch,0.f);else sf::decode(data,int(n*in_ch),capture_scratch.data(),in_float,in_bits,in_valid);
                bridge.push(capture_scratch.data(),int(n));
            }catch(...) {capture->ReleaseBuffer(n);throw;}
            check(capture->ReleaseBuffer(n),"Release capture buffer");
        }
    };
    StreamStop in_stop{in_client.Get()},out_stop{out_client.Get()};
    if(p->stop)throw Cancelled{};
    {BYTE* bytes=nullptr;check(render->GetBuffer(out_frames,&bytes),"Prime output buffer");check(render->ReleaseBuffer(out_frames,AUDCLNT_BUFFERFLAGS_SILENT),"Submit silent prime");}
    ResetEvent(in_event.value);ResetEvent(out_event.value);
    check(in_client->Start(),"Start WASAPI capture");in_stop.running=true;check(out_client->Start(),"Start WASAPI output");out_stop.running=true;started=true;
    {std::lock_guard<std::mutex> lock(p->status_lock);p->status.state=SF_PLAYING;++p->status.started_sequence;p->status.input_channels=in_ch;p->status.input_rate=in_rate;p->status.output_channels=out_ch;p->status.output_rate=out_rate;p->status.exclusive_active=exclusive?1:0;p->status.buffer_frames=int(out_frames);}
    HANDLE events[]={p->stop_event,in_event.value,out_event.value};
    for(;;) {
        DWORD wait=WaitForMultipleObjects(3,events,FALSE,2000);
        if(wait==WAIT_OBJECT_0 || p->stop)throw Cancelled{};
        if(wait==WAIT_TIMEOUT)throw std::runtime_error("No audio events for 2 seconds; a device may be unavailable, disconnected or blocked by OS microphone privacy settings");
        if(wait!=WAIT_OBJECT_0+1 && wait!=WAIT_OBJECT_0+2)throw std::runtime_error("WASAPI live event wait failed");
        drain_capture();
        UINT32 available=out_frames;
        if(!exclusive) {UINT32 padding=0;check(out_client->GetCurrentPadding(&padding),"Get output padding");if(padding>out_frames)throw std::runtime_error("Invalid device buffer padding");available=out_frames-padding;}
        else if(WaitForSingleObject(out_event.value,0)!=WAIT_OBJECT_0 && wait!=WAIT_OBJECT_0+2)available=0;
        if(available)submit(available);
        auto stats=bridge.snapshot();float peak=bridge.take_peak();
        std::lock_guard<std::mutex> lock(p->status_lock);p->status.captured_frames=stats.captured;p->status.rendered_frames=stats.rendered;p->status.underruns=stats.underruns;p->status.overruns=stats.overruns;p->status.latency_frames=stats.fill_frames;p->status.input_peak=std::max(p->status.input_peak,peak);
    }
}
#endif
#ifdef __APPLE__
#include "core_audio.inc"
#endif
void work(sf_player* p,std::shared_ptr<const sf::Pcm> pcm,std::string id,int selected,bool exclusive,int buffer_ms) {
    bool started=false;int reason=SF_DEVICE_ERROR;std::string error;
    try {
#if defined(_WIN32) || defined(__APPLE__)
        reason=run_device(p,std::move(pcm),id,selected,exclusive,buffer_ms,started);
#else
        (void)pcm;(void)id;(void)selected;(void)exclusive;(void)buffer_ms;throw std::runtime_error("Direct-device backend unsupported on this platform");
#endif
    }
#if defined(_WIN32) || defined(__APPLE__)
    catch(const Cancelled&) {reason=SF_CANCELLED;}
#endif
    catch(const std::exception& e) {reason=p->stop?SF_CANCELLED:SF_DEVICE_ERROR;error=e.what();}
    catch(...) {reason=SF_DEVICE_ERROR;error="Unknown audio worker failure";}
    std::lock_guard<std::mutex> lock(p->status_lock);p->status.state=reason==SF_DEVICE_ERROR?SF_FAILED:SF_STOPPED;p->status.reason=reason;p->status.exclusive_active=0;text(p->status.error_utf8,1024,error);if(started)++p->status.ended_sequence;
}
void work_live(sf_live* p,std::string in_id,int in_sel,std::string out_id,int out_sel,bool exclusive,int buffer_ms) {
    bool started=false;int reason=SF_DEVICE_ERROR;std::string error;
    try {
#if defined(_WIN32) || defined(__APPLE__)
        reason=run_live(p,in_id,in_sel,out_id,out_sel,exclusive,buffer_ms,started);
#else
        (void)in_id;(void)in_sel;(void)out_id;(void)out_sel;(void)exclusive;(void)buffer_ms;throw std::runtime_error("Live input backend unsupported on this platform");
#endif
    }
#if defined(_WIN32) || defined(__APPLE__)
    catch(const Cancelled&) {reason=SF_CANCELLED;}
#endif
    catch(const std::exception& e) {reason=p->stop?SF_CANCELLED:SF_DEVICE_ERROR;error=e.what();}
    catch(...) {reason=SF_DEVICE_ERROR;error="Unknown live audio worker failure";}
    std::lock_guard<std::mutex> lock(p->status_lock);p->status.state=reason==SF_DEVICE_ERROR?SF_FAILED:SF_STOPPED;p->status.reason=reason;p->status.exclusive_active=0;text(p->status.error_utf8,1024,error);if(started)++p->status.ended_sequence;
}
}
extern "C" {
uint32_t sf_audio_abi_version(void) {return 1;}
uint32_t sf_live_abi_version(void) {return 1;}
int32_t sf_input_devices_json(char* output,int32_t capacity,char* error,int32_t error_capacity) {
    try {
#if defined(_WIN32) || defined(__APPLE__)
        std::string value=std::async(std::launch::async,[]{return devices(true);}).get();if(value.size()>1024*1024)throw std::runtime_error("Device inventory limit exceeded");int required=int(value.size()+1);if(output && capacity>=required)text(output,capacity,value);return required;
#else
        (void)output;(void)capacity;throw std::runtime_error("Live input backend unsupported on this platform");
#endif
    }catch(const std::exception& e) {text(error,error_capacity,e.what());return -1;}catch(...) {text(error,error_capacity,"Input device enumeration failure");return -1;}
}
sf_live* sf_live_create(void) {try{auto p=std::make_unique<sf_live>();
#ifdef _WIN32
    if(!p->stop_event)return nullptr;
#endif
    return p.release();}catch(...){return nullptr;}}
int32_t sf_live_start(sf_live* p,const char* in_id,int32_t in_sel,const char* out_id,int32_t out_sel,int32_t exclusive,int32_t buffer_ms,float gain) {
    if(!p)return 0;try {
        std::lock_guard<std::mutex> guard(p->control);cancel_join(p);p->stop=false;
#ifdef _WIN32
        ResetEvent(p->stop_event);
#endif
        {std::lock_guard<std::mutex> lock(p->status_lock);auto starts=p->status.started_sequence,ends=p->status.ended_sequence;p->status={};p->status.started_sequence=starts;p->status.ended_sequence=ends;p->status.state=SF_STARTING;}
        if(in_sel<0 || in_sel>64 || out_sel<0 || out_sel>64 || buffer_ms<0 || buffer_ms>2000 || !std::isfinite(gain) || gain<0 || gain>4)throw std::runtime_error("Invalid live channel, buffer or gain request");
        p->gain=gain;p->worker=std::thread(work_live,p,in_id?std::string(in_id):std::string(),in_sel,out_id?std::string(out_id):std::string(),out_sel,exclusive!=0,buffer_ms);return 1;
    }catch(const std::exception& e){std::lock_guard<std::mutex> lock(p->status_lock);p->status.state=SF_FAILED;p->status.reason=SF_DEVICE_ERROR;text(p->status.error_utf8,1024,e.what());return 0;}catch(...){return 0;}
}
void sf_live_gain(sf_live* p,float gain) {if(p && std::isfinite(gain) && gain>=0 && gain<=4)p->gain=gain;}
void sf_live_status_get(sf_live* p,sf_live_status* output) {if(!output)return;if(!p){*output={};return;}std::lock_guard<std::mutex> lock(p->status_lock);*output=p->status;p->status.input_peak=0;}
void sf_live_stop(sf_live* p) {if(!p)return;std::lock_guard<std::mutex> guard(p->control);cancel_join(p);std::lock_guard<std::mutex> lock(p->status_lock);p->status.state=SF_STOPPED;p->status.reason=SF_CANCELLED;p->status.exclusive_active=0;}
void sf_live_destroy(sf_live* p) {if(!p)return;sf_live_stop(p);delete p;}
int32_t sf_devices_json(char* output,int32_t capacity,char* error,int32_t error_capacity) {
    try {
#if defined(_WIN32) || defined(__APPLE__)
        std::string value=std::async(std::launch::async,[]{return devices();}).get();if(value.size()>1024*1024)throw std::runtime_error("Device inventory limit exceeded");int required=int(value.size()+1);if(output && capacity>=required)text(output,capacity,value);return required;
#else
        (void)output;(void)capacity;throw std::runtime_error("Direct-device backend unsupported on this platform");
#endif
    }catch(const std::exception& e) {text(error,error_capacity,e.what());return -1;}catch(...) {text(error,error_capacity,"Device enumeration failure");return -1;}
}
sf_buffer* sf_buffer_create(const float* samples,int64_t count,int32_t channels,int32_t rate,char* error,int32_t capacity) {
    try {return new sf_buffer{sf::prepare(samples,count,channels,rate)};}catch(const std::exception& e){text(error,capacity,e.what());return nullptr;}catch(...){text(error,capacity,"PCM allocation failed");return nullptr;}
}
void sf_buffer_destroy(sf_buffer* b) {delete b;}
int32_t sf_buffer_render(sf_buffer* b,float* output,int32_t frames,int32_t channels,int32_t rate,int32_t selected,int32_t loop,double* position) {
    if(!b || !position)return -1;try{return sf::render(*b->pcm,*position,output,frames,channels,rate,selected,loop!=0)?1:0;}catch(...){return -1;}
}
sf_player* sf_player_create(void) {try{auto p=std::make_unique<sf_player>();
#ifdef _WIN32
    if(!p->stop_event)return nullptr;
#endif
    return p.release();}catch(...){return nullptr;}}
int32_t sf_player_start(sf_player* p,sf_buffer* b,const char* id,int32_t selected,int32_t exclusive,int32_t buffer_ms,int32_t loop) {
    if(!p || !b)return 0;try {
        std::lock_guard<std::mutex> guard(p->control);cancel_join(p);p->stop=false;p->loop=loop!=0;
#ifdef _WIN32
        ResetEvent(p->stop_event);
#endif
        {std::lock_guard<std::mutex> lock(p->status_lock);auto starts=p->status.started_sequence,ends=p->status.ended_sequence;p->status={};p->status.started_sequence=starts;p->status.ended_sequence=ends;p->status.state=SF_STARTING;}
        if(selected<0 || selected>64 || buffer_ms<0 || buffer_ms>2000)throw std::runtime_error("Invalid output channel or buffer request");
        p->worker=std::thread(work,p,b->pcm,id?std::string(id):std::string(),selected,exclusive!=0,buffer_ms);return 1;
    }catch(const std::exception& e){std::lock_guard<std::mutex> lock(p->status_lock);p->status.state=SF_FAILED;p->status.reason=SF_DEVICE_ERROR;text(p->status.error_utf8,1024,e.what());return 0;}catch(...){return 0;}
}
void sf_player_loop(sf_player* p,int32_t loop) {if(p)p->loop=loop!=0;}
void sf_player_status(sf_player* p,sf_status* output) {if(!output)return;if(!p){*output={};return;}std::lock_guard<std::mutex> lock(p->status_lock);*output=p->status;}
void sf_player_stop(sf_player* p) {if(!p)return;std::lock_guard<std::mutex> guard(p->control);cancel_join(p);std::lock_guard<std::mutex> lock(p->status_lock);p->status.state=SF_STOPPED;p->status.reason=SF_CANCELLED;p->status.exclusive_active=0;}
void sf_player_destroy(sf_player* p) {if(!p)return;sf_player_stop(p);delete p;}
}
