// Windows-only, device-free tests of the real WASAPI output initialization helper.
// Compile backend into this executable so its private helper needs no public test ABI.
#include "../src/audio.cpp"
#include <iostream>
#include <stdexcept>

namespace {
struct Plan {
    HRESULT supported=S_OK, exclusive_result=S_OK, aligned_result=S_OK, shared_result=S_OK;
    int activated=0, exclusive_calls=0, shared_calls=0, fail_activation=0;
    bool align=false, latency_available=true;
    REFERENCE_TIME last_duration=0, last_period=0;
    DWORD last_flags=0;
};
struct FakeClient final : IAudioClient {
    ULONG refs=1;Plan& plan;int generation;
    FakeClient(Plan& p,int g):plan(p),generation(g){}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID,void**) override {return E_NOINTERFACE;}
    ULONG STDMETHODCALLTYPE AddRef() override {return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override {auto n=--refs;if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE Initialize(AUDCLNT_SHAREMODE mode,DWORD flags,REFERENCE_TIME duration,REFERENCE_TIME period,const WAVEFORMATEX*,LPCGUID) override {
        plan.last_duration=duration;plan.last_period=period;plan.last_flags=flags;
        if(mode==AUDCLNT_SHAREMODE_SHARED){++plan.shared_calls;return plan.shared_result;}
        ++plan.exclusive_calls;
        if(plan.align && generation==1)return AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED;
        return plan.align?plan.aligned_result:plan.exclusive_result;
    }
    HRESULT STDMETHODCALLTYPE GetBufferSize(UINT32* frames) override {*frames=480;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetStreamLatency(REFERENCE_TIME* latency) override {*latency=123456;return plan.latency_available?S_OK:E_FAIL;}
    HRESULT STDMETHODCALLTYPE GetCurrentPadding(UINT32*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE IsFormatSupported(AUDCLNT_SHAREMODE,const WAVEFORMATEX*,WAVEFORMATEX**) override {return plan.supported;}
    HRESULT STDMETHODCALLTYPE GetMixFormat(WAVEFORMATEX**) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetDevicePeriod(REFERENCE_TIME* period,REFERENCE_TIME* minimum) override {*period=100000;*minimum=30000;return S_OK;}
    HRESULT STDMETHODCALLTYPE Start() override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE Stop() override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE Reset() override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetEventHandle(HANDLE) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetService(REFIID,void**) override {return E_NOTIMPL;}
};
struct FakeDevice final : IMMDevice {
    Plan& plan;explicit FakeDevice(Plan& p):plan(p){}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID,void**) override {return E_NOINTERFACE;}
    ULONG STDMETHODCALLTYPE AddRef() override {return 1;}
    ULONG STDMETHODCALLTYPE Release() override {return 1;}
    HRESULT STDMETHODCALLTYPE Activate(REFIID,DWORD,PROPVARIANT*,void** output) override {
        ++plan.activated;*output=nullptr;
        if(plan.activated==plan.fail_activation)return E_FAIL;
        *output=new FakeClient(plan,plan.activated);return S_OK;
    }
    HRESULT STDMETHODCALLTYPE OpenPropertyStore(DWORD,IPropertyStore**) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetId(LPWSTR*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetState(DWORD*) override {return E_NOTIMPL;}
};
}
int main() {
    try {
        int checks=0;auto require=[&](bool value){if(!value)throw std::runtime_error("Negotiation check failed #"+std::to_string(checks+1));++checks;};
        auto test=[&](Plan& plan,bool requested,bool expected,bool fail=false) {
            FakeDevice device(plan);ComPtr<IAudioClient> client;
            check(device.Activate(__uuidof(IAudioClient),0,nullptr,reinterpret_cast<void**>(client.GetAddressOf())),"Fake activate");
            WAVEFORMATEX format{};format.nSamplesPerSec=48000;
            bool active=requested,threw=false;
            try {initialize_output(&device,client,&format,active,requested?0:15);}catch(const std::exception&){threw=true;}
            require(threw==fail);if(!threw){require(active==expected);require(plan.latency_available?std::abs(output_latency(client.Get())-12.3456)<1e-8:output_latency(client.Get())==-1);}
        };
        Plan shared;test(shared,false,false);require(shared.activated==1 && shared.shared_calls==1 && shared.exclusive_calls==0 && shared.last_period==0 && shared.last_duration==150000);
        Plan exclusive;test(exclusive,true,true);require(exclusive.activated==1 && exclusive.exclusive_calls==1 && exclusive.shared_calls==0 && exclusive.last_duration==100000 && exclusive.last_period==100000);
        Plan unsupported;unsupported.supported=AUDCLNT_E_UNSUPPORTED_FORMAT;test(unsupported,true,false);require(unsupported.activated==2 && unsupported.exclusive_calls==0 && unsupported.shared_calls==1 && unsupported.last_period==0 && (unsupported.last_flags&AUDCLNT_STREAMFLAGS_NOPERSIST));
        Plan refused;refused.exclusive_result=AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED;test(refused,true,false);require(refused.activated==2 && refused.exclusive_calls==1 && refused.shared_calls==1);
        Plan aligned;aligned.align=true;test(aligned,true,true);require(aligned.activated==2 && aligned.exclusive_calls==2 && aligned.shared_calls==0 && aligned.last_duration==100000);
        Plan aligned_failure;aligned_failure.align=true;aligned_failure.aligned_result=AUDCLNT_E_DEVICE_IN_USE;test(aligned_failure,true,false);require(aligned_failure.activated==3 && aligned_failure.exclusive_calls==2 && aligned_failure.shared_calls==1);
        Plan alignment_activation_failed;alignment_activation_failed.align=true;alignment_activation_failed.fail_activation=2;test(alignment_activation_failed,true,false);require(alignment_activation_failed.activated==3 && alignment_activation_failed.exclusive_calls==1 && alignment_activation_failed.shared_calls==1);
        Plan shared_activation_failed;shared_activation_failed.supported=AUDCLNT_E_UNSUPPORTED_FORMAT;shared_activation_failed.fail_activation=2;test(shared_activation_failed,true,false,true);require(shared_activation_failed.activated==2 && shared_activation_failed.shared_calls==0);
        Plan both_failed;both_failed.supported=AUDCLNT_E_UNSUPPORTED_FORMAT;both_failed.shared_result=AUDCLNT_E_DEVICE_INVALIDATED;test(both_failed,true,false,true);require(both_failed.activated==2 && both_failed.shared_calls==1);
        Plan no_latency;no_latency.latency_available=false;test(no_latency,false,false);
        // Diagnostic lifecycle uses only cached fields, never an OS query.
        sf_player player;sf_live live;
        player.output_latency_ms=12.3456;player.output_mode=SF_OUTPUT_SHARED_FALLBACK;
        live.output_latency_ms=7;live.output_mode=SF_OUTPUT_EXCLUSIVE;
        for(int state:{SF_STARTING,SF_PLAYING,SF_DRAINING,SF_STOPPED,SF_FAILED}) {
            player.status.state=state;live.status.state=state;bool active=state==SF_PLAYING || state==SF_DRAINING;
            require(sf_player_output_latency_ms(&player)==(active?12.3456:-1));
            require(sf_live_output_latency_ms(&live)==(active?7:-1));
            require(sf_player_output_mode(&player)==(active?SF_OUTPUT_SHARED_FALLBACK:SF_OUTPUT_INACTIVE));
            require(sf_live_output_mode(&live)==(active?SF_OUTPUT_EXCLUSIVE:SF_OUTPUT_INACTIVE));
        }
        std::cout<<checks<<" mocked WASAPI negotiation checks passed; no OS devices opened\n";
        return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
