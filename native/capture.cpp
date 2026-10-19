#include "capture.h"
#include <algorithm>
#include <cstring>
#include <fstream>
#include <regex>
#include <stdexcept>

static std::string fileText(const std::filesystem::path& path){
    std::ifstream input(path);if(!input)throw std::runtime_error("Cannot open capture state: "+path.string());
    return {std::istreambuf_iterator<char>(input),{}};
}
static uint32_t number(const std::string& text,const std::string& key){
    std::smatch match;if(!std::regex_search(text,match,std::regex("\""+key+"\"\\s*:\\s*(\\d+)")))throw std::runtime_error("Capture metadata missing: "+key);
    return static_cast<uint32_t>(std::stoul(match[1]));
}
static uint16_t word(const uint8_t* p){uint16_t v;std::memcpy(&v,p,2);return v;}
static uint32_t dword(const uint8_t* p){uint32_t v;std::memcpy(&v,p,4);return v;}
static constexpr std::array<uint8_t,16> magic={'L','L','V','R','R','I','N','G','v','2',0,0,0,0,0,0};

Capture::Capture(const std::filesystem::path& runtime){
    const auto state=fileText(runtime/"bridge-state.json"),manifest=fileText(runtime/"hook-manifest.json");
    pid_=number(state,"emulator_pid");hookSize_=number(manifest,"hook_size");
    caveSegment_=static_cast<uint16_t>(number(manifest,"hook_segment_relative"));
    ringStart_=number(manifest,"ring_start");capacity_=number(manifest,"capacity");
    nativeEffects_=manifest.find("transformed-sprites")!=std::string::npos;
    if(nativeEffects_)bufferSegment_=number(manifest,"buffer_segment_relative");
    process_=OpenProcess(PROCESS_QUERY_INFORMATION|PROCESS_VM_READ,FALSE,pid_);
    if(!process_)throw std::runtime_error("Cannot open the recorded emulator process");
    try{locate();}catch(...){CloseHandle(process_);process_=nullptr;throw;}
}
Capture::~Capture(){if(process_)CloseHandle(process_);}
std::vector<uint8_t> Capture::read(uintptr_t address,size_t size) const{
    std::vector<uint8_t> data(size);SIZE_T received{};
    if(!ReadProcessMemory(process_,reinterpret_cast<void*>(address),data.data(),size,&received)||received!=size)throw std::runtime_error("Emulator capture memory is unavailable");
    return data;
}
void Capture::locate(){
    MEMORY_BASIC_INFORMATION region{};uintptr_t address=0;size_t inspected=0;
    const std::array<uint8_t,10> prefix={0x9c,0x50,0x53,0x51,0x52,0x56,0x57,0x55,0x1e,0x06};
    while(VirtualQueryEx(process_,reinterpret_cast<void*>(address),&region,sizeof(region))){
        const auto base=reinterpret_cast<uintptr_t>(region.BaseAddress);const size_t size=region.RegionSize;
        if(!size||base+size<=address)break;address=base+size;
        const DWORD protection=region.Protect&255;
        if(region.State!=MEM_COMMIT||region.Type!=MEM_PRIVATE||(region.Protect&PAGE_GUARD)||size<65536||size>128*1024*1024||
           (protection!=PAGE_READWRITE&&protection!=PAGE_WRITECOPY&&protection!=PAGE_EXECUTE_READWRITE&&protection!=PAGE_EXECUTE_WRITECOPY))continue;
        for(size_t offset=0;offset<size;offset+=4*1024*1024){
            std::vector<uint8_t> block;
            try{block=read(base+offset,std::min<size_t>(4*1024*1024+16,size-offset));}catch(...){continue;}
            inspected+=block.size();auto position=block.begin();
            while((position=std::search(position,block.end(),magic.begin(),magic.end()))!=block.end()){
                const auto ring=base+offset+static_cast<size_t>(position-block.begin())-0xe0;
                try{
                    const auto hook=read(ring,hookSize_);
                    if(!std::equal(prefix.begin(),prefix.end(),hook.begin()))throw std::runtime_error("Unexpected capture hook");
                    const uint16_t game=word(hook.data()+hook.size()-2);
                    const uintptr_t guest=ring-(static_cast<uintptr_t>(game)+caveSegment_)*16;
                    const auto jump=read(guest+static_cast<uintptr_t>(game)*16+0x8cbb,5);
                    if(jump[0]==0xea&&word(jump.data()+1)==0&&word(jump.data()+3)==uint16_t(game+caveSegment_)){
                        ring_=ring;guest_=guest;gameSegment_=game;return;
                    }
                }catch(...){}
                ++position;
            }
            if(inspected>256*1024*1024)throw std::runtime_error("Capture hook not found in bounded memory scan");
        }
    }
    throw std::runtime_error("Capture hook not loaded");
}
bool Capture::readFrame(SceneFrame& frame){
    for(int attempt=0;attempt<4;attempt++){
        const auto data=read(ring_,65536);
        if(!std::equal(magic.begin(),magic.end(),data.begin()+0xe0))throw std::runtime_error("Capture signature changed");
        const uint16_t cursor=word(data.data()+0xf0);const uint32_t total=dword(data.data()+0xf2);
        if(cursor<ringStart_||cursor%16)throw std::runtime_error("Invalid capture cursor");
        SceneFrame candidate;bool foundEnd=false,foundBegin=false;uint16_t page=0;
        for(uint32_t i=0;i<std::min(total,capacity_);i++){
            const uint32_t span=65536-ringStart_;
            const uint32_t offset=ringStart_+((cursor-ringStart_+span-((i+1)*16)%span)%span);
            const uint8_t* record=data.data()+offset;const uint16_t segment=word(record+4),sprite=word(record+6),sequence=word(record+8);
            const bool marker=segment==65535&&(sprite==1||sprite==2);
            if(!foundEnd){if(marker&&sprite==2){foundEnd=true;candidate.sequence=sequence;candidate.eventOrder=total-i-1;page=word(record+12);}continue;}
            if(sequence!=candidate.sequence)break;
            if(marker){foundBegin=sprite==1;break;}
            if(word(record+12)==page)candidate.sprites.push_back({static_cast<int16_t>(word(record)),static_cast<int16_t>(word(record+2)),
                static_cast<uint16_t>(segment-gameSegment_),sprite,sequence,page,nativeEffects_&&word(record+14)==65535});
        }
        if(!foundBegin)continue;
        std::reverse(candidate.sprites.begin(),candidate.sprites.end());
        if(nativeEffects_){
            const auto before=read(ring_+0xf8,8);const uint16_t bank=word(before.data()),publication=word(before.data()+2);
            if(!publication||(publication&1)||word(before.data()+4)!=candidate.sequence||(bank!=0&&bank!=0x8000))continue;
            const auto planes=read(guest_+(static_cast<uintptr_t>(gameSegment_)+bufferSegment_)*16+bank,32048);
            if(read(ring_+0xf8,8)!=before)continue;
            candidate.publication=publication;candidate.hasEffects=true;
            for(size_t byte=0;byte<8000;byte++)for(int bit=0;bit<8;bit++){
                uint8_t colour=0;for(int plane=0;plane<4;plane++)colour|=((planes[byte+plane*8000]>>(7-bit))&1)<<plane;
                const size_t pixel=(byte*8+bit)*4;
                for(int channel=0;channel<3;channel++)candidate.effects[pixel+channel]=static_cast<uint8_t>((planes[32000+colour*3+channel]*255+31)/63);
                candidate.effects[pixel+3]=colour?255:0;
            }
        }
        frame=std::move(candidate);return true;
    }
    return false;
}
void Capture::input(uint8_t bits,uint8_t scan,bool writeScan){
    HANDLE write=OpenProcess(PROCESS_VM_OPERATION|PROCESS_VM_WRITE,FALSE,pid_);
    if(!write)throw std::runtime_error("Cannot send input to recorded emulator");
    const uintptr_t base=guest_+(static_cast<uintptr_t>(gameSegment_)+0x2559)*16;SIZE_T written{};
    bool success=WriteProcessMemory(write,reinterpret_cast<void*>(base+0x588),&bits,1,&written)&&written==1;
    if(writeScan)success=success&&WriteProcessMemory(write,reinterpret_cast<void*>(base+0x587),&scan,1,&written)&&written==1;
    CloseHandle(write);if(!success)throw std::runtime_error("Native input write failed");
}
