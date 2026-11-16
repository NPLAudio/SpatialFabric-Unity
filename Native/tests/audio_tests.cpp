// Silent native tests. Never open a default/valid device or submit sound to hardware.
#include "spatialfabric_audio.h"
#include "pcm.h"
#include <cassert>
#include <chrono>
#include <cmath>
#include <iostream>
#include <limits>
#include <thread>
#include <string>
#include <vector>
int main() {
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
    std::cout<<checks<<" silent native checks passed; no valid output device opened\n";
}
