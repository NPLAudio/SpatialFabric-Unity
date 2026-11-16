// Original engine-independent backend; MIT. No Unreal or Unity headers.
#define SF_AUDIO_BUILD
#include "spatialfabric_audio.h"
#include "pcm.h"
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
std::string devices() {
    ComScope com;ComPtr<IMMDeviceEnumerator> enumerator;check(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&enumerator)),"Create device enumerator");
    ComPtr<IMMDevice> def;std::string default_id;if(SUCCEEDED(enumerator->GetDefaultAudioEndpoint(eRender,eConsole,&def))) {DeviceId id;check(def->GetId(&id.value),"Default device ID");default_id=utf8(id.value);}
    ComPtr<IMMDeviceCollection> collection;check(enumerator->EnumAudioEndpoints(eRender,DEVICE_STATE_ACTIVE,&collection),"Enumerate output devices");UINT count=0;check(collection->GetCount(&count),"Count devices");
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
}
extern "C" {
uint32_t sf_audio_abi_version(void) {return 1;}
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
