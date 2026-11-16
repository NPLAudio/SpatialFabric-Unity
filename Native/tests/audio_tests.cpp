// Silent native tests. Never open a default/valid device or submit sound to hardware.
#include "spatialfabric_audio.h"
#include "pcm.h"
#include "live.h"
#include <cassert>
#include <chrono>
#include <cmath>
#include <iostream>
#include <limits>
#include <thread>
#include <string>
#include <vector>
int run() {
    int checks=0;auto check=[&](bool ok){if(!ok)throw std::runtime_error("Native check failed #"+std::to_string(checks+1));++checks;};
    float source[]={.25f,-.5f,.75f,-1.f};char error[1024]{};
    auto pcm=sf::prepare(source,4,2,48000);double p=0;float out[12]{};
    check(sf::render(*pcm,p,out,3,4,48000,0,false));check(out[0]==.25f && out[1]==-.5f && out[2]==0 && out[3]==0 && out[4]==.75f && out[5]==-1 && out[8]==0);
    p=0;sf::render(*pcm,p,out,2,4,48000,3,false);check(out[0]==0 && out[1]==0 && out[2]==.25f && out[6]==.75f);
    p=0;check(!sf::render(*pcm,p,out,3,2,48000,0,true));check(out[4]==.25f && out[5]==-.5f);
    p=0;sf::render(*pcm,p,out,4,2,96000,0,false);check(std::abs(out[2]-.5f)<1e-6f && out[6]==.75f);
    p=0;sf::render(*pcm,p,out,2,2,24000,0,false);check(out[2]==0 && p==2);
    bool invalid=false;try{p=0;sf::render(*pcm,p,out,1,2,48000,3,false);}catch(...){invalid=true;}check(invalid);
    float nan=std::numeric_limits<float>::quiet_NaN();check(sf_buffer_create(&nan,1,1,48000,error,1024)==nullptr);
    check(sf_buffer_create(source,3,2,48000,error,1024)==nullptr);check(sf_buffer_create(source,4,2,0,error,1024)==nullptr);
    uint8_t encoded[16]{};float endpoints[]={-1,0,1,std::numeric_limits<float>::infinity()};
    sf::encode(endpoints,4,encoded,false,16,16);check(encoded[0]==1 && encoded[1]==128 && encoded[4]==255 && encoded[5]==127 && encoded[6]==0 && encoded[7]==0);
    sf::encode(endpoints,4,encoded,false,32,24);check(encoded[0]==0 && encoded[4]==0 && encoded[8]==0);
    auto b=sf_buffer_create(source,4,2,48000,error,1024);check(b!=nullptr);source[0]=1;p=0;check(sf_buffer_render(b,out,1,2,48000,0,0,&p)==0 && out[0]==.25f); // Immutable copy.
    check(sf_buffer_render(b,out,1,2,48000,99,0,&p)==-1);
    check(sf_audio_abi_version()==1);
    auto player=sf_player_create();check(player!=nullptr);sf_status status{};sf_player_status(player,&status);check(status.state==SF_STOPPED && status.started_sequence==0);
    check(sf_player_start(player,b,"spatialfabric-intentionally-invalid-device-id-no-fallback",0,0,0,0)==1);
    sf_buffer_destroy(b); // Worker must retain immutable PCM independently of caller handle.
    auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(5);
    do {std::this_thread::sleep_for(std::chrono::milliseconds(5));sf_player_status(player,&status);}while(status.state==SF_STARTING && std::chrono::steady_clock::now()<deadline);
    check(status.state==SF_FAILED && status.reason==SF_DEVICE_ERROR && status.started_sequence==0 && status.ended_sequence==0 && status.error_utf8[0]);
    sf_player_stop(player);sf_player_status(player,&status);check(status.state==SF_STOPPED && status.reason==SF_CANCELLED);
    sf_player_destroy(player);
    // Repeated cancellation/lifetime stress, still only an invalid device ID.
    float mono[]={.1f};b=sf_buffer_create(mono,1,1,48000,error,1024);
    for(int i=0;i<50;++i){player=sf_player_create();sf_player_start(player,b,"spatialfabric-invalid-device-no-fallback",0,0,0,0);sf_player_stop(player);sf_player_destroy(player);}
    sf_buffer_destroy(b);check(true);
    // ---- Live input bridge: all offline; no capture or render device is ever opened. ----
    {
        auto fill=[](std::vector<float>& v,int frames,int channels,std::initializer_list<float> values){v.assign(size_t(frames)*channels,0.f);for(int f=0;f<frames;++f){int c=0;for(float x:values){if(c<channels)v[size_t(f)*channels+c]=x;++c;}}};
        std::vector<float> in,o(2000*2);
        fill(in,2000,2,{.5f,-.25f});sf::LiveBridge all(2,48000,2,48000,0,0,20);
        all.push(in.data(),100);check(all.pull(o.data(),50,1.f)==0 && o[0]==0 && all.snapshot().underruns==0 && !all.snapshot().primed); // not primed: silence, not an underrun
        all.push(in.data(),2000);check(all.pull(o.data(),500,1.f)==0 && o[0]==.5f && o[1]==-.25f && o[998]==.5f);
        sf::LiveBridge routed(2,48000,2,48000,0,2,20);routed.push(in.data(),2000);routed.pull(o.data(),10,1.f);check(o[0]==0 && o[1]==.5f && o[2]==0 && o[3]==.5f); // output 2 gets first source channel only
        sf::LiveBridge single(2,48000,2,48000,2,0,20);single.push(in.data(),2000);single.pull(o.data(),10,1.f);check(o[0]==-.25f && o[1]==0); // input channel 2 -> corresponding output 1
        sf::LiveBridge both(2,48000,3,48000,2,3,20);both.push(in.data(),2000);both.pull(o.data(),4,1.f);check(o[0]==0 && o[1]==0 && o[2]==-.25f);
        std::vector<float> ramp;fill(ramp,1500,1,{.25f});sf::LiveBridge half(1,24000,1,48000,0,0,20);half.push(ramp.data(),1500);half.pull(o.data(),960,1.f);auto fill_after=half.snapshot().fill_frames;check(fill_after>=1000 && fill_after<=1040); // 24 kHz -> 48 kHz consumes ~half as many frames
        std::vector<float> exact;fill(exact,960,2,{.1f,.1f});sf::LiveBridge under(2,48000,2,48000,0,0,20);under.push(exact.data(),960);int silent=under.pull(o.data(),2000,1.f);check(silent>900 && silent<1200 && under.snapshot().underruns==1 && o[2*1999]==0 && !under.snapshot().primed);
        std::vector<float> burst;fill(burst,1000,1,{.3f});sf::LiveBridge over(1,48000,1,48000,0,0,5);for(int i=0;i<3;++i)over.push(burst.data(),1000);check(over.snapshot().overruns>=1 && over.snapshot().fill_frames<=1920 && over.snapshot().captured==3000);
        std::vector<float> loud;fill(loud,2000,1,{.5f});loud[10]=std::numeric_limits<float>::quiet_NaN();sf::LiveBridge boost(1,48000,1,48000,0,0,20);boost.push(loud.data(),2000);boost.pull(o.data(),20,4.f);check(o[0]==1.f && o[1]==1.f && boost.take_peak()==.5f && boost.take_peak()==0.f); // gain clamps at full scale; NaN is sanitized
        bool rejected=false;try{sf::LiveBridge bad(2,48000,2,48000,3,0,20);}catch(...){rejected=true;}check(rejected);rejected=false;try{sf::LiveBridge bad(2,48000,2,48000,0,3,20);}catch(...){rejected=true;}check(rejected);rejected=false;try{all.pull(o.data(),1,5.f);}catch(...){rejected=true;}check(rejected);
        float values[]={-1.f,0.f,.5f,.999f};uint8_t bytes[16]{};float back[4]{};
        for(int bits:{16,24,32}){sf::encode(values,4,bytes,false,bits,bits);sf::decode(bytes,4,back,false,bits,bits);check(std::abs(back[0]+1.f)<1e-3f && back[1]==0.f && std::abs(back[2]-.5f)<1e-3f && std::abs(back[3]-.999f)<2e-3f);}
        sf::encode(values,4,bytes,true,32,32);sf::decode(bytes,4,back,true,32,32);check(back[2]==.5f && back[3]==.999f);
        sf::encode(values,4,bytes,false,32,24);sf::decode(bytes,4,back,false,32,24);check(std::abs(back[2]-.5f)<1e-5f); // valid-bits packing
        int32_t need=sf_input_devices_json(nullptr,0,error,1024);
#if defined(_WIN32) || defined(__APPLE__)
        check(need>1); // explicit inventory only; opens no stream
#else
        check(need==-1 && error[0]);
#endif
        check(sf_live_abi_version()==1);
        auto live=sf_live_create();check(live!=nullptr);sf_live_status ls{};sf_live_status_get(live,&ls);check(ls.state==SF_STOPPED && ls.started_sequence==0);
        check(sf_live_start(live,"spatialfabric-invalid-input-no-fallback",0,"spatialfabric-invalid-output-no-fallback",0,0,0,1.f)==1);
        auto live_deadline=std::chrono::steady_clock::now()+std::chrono::seconds(5);
        do {std::this_thread::sleep_for(std::chrono::milliseconds(5));sf_live_status_get(live,&ls);}while(ls.state==SF_STARTING && std::chrono::steady_clock::now()<live_deadline);
        check(ls.state==SF_FAILED && ls.reason==SF_DEVICE_ERROR && ls.started_sequence==0 && ls.captured_frames==0 && ls.error_utf8[0]);
        check(sf_live_start(live,nullptr,65,nullptr,0,0,0,1.f)==0 && sf_live_start(live,nullptr,0,nullptr,0,0,0,std::numeric_limits<float>::quiet_NaN())==0 && sf_live_start(live,nullptr,0,nullptr,0,0,0,5.f)==0);
        sf_live_stop(live);sf_live_status_get(live,&ls);check(ls.state==SF_STOPPED && ls.reason==SF_CANCELLED);sf_live_destroy(live);
        for(int i=0;i<50;++i){live=sf_live_create();sf_live_start(live,"spatialfabric-invalid-input",0,"spatialfabric-invalid-output",0,0,0,1.f);sf_live_stop(live);sf_live_destroy(live);}
    }
    std::cout<<checks<<" silent native checks passed; no valid output device opened\n";
    return 0;
}
int main() {try{return run();}catch(const std::exception& e){std::cerr<<"FAILED: "<<e.what()<<std::endl;return 1;}}
