#include "capture.h"
#include "renderer.h"
#include "xr_session.h"
#include <chrono>
#include <iostream>
#include <memory>
#include <thread>

static bool closing=false;
static std::filesystem::path defaultRuntime(){
    wchar_t executable[32768]{};GetModuleFileNameW(nullptr,executable,32768);
    for(auto folder=std::filesystem::path(executable).parent_path();!folder.empty();folder=folder.parent_path()){
        if(std::filesystem::exists(folder/"artifacts/effects-probe/bridge-state.json"))return folder/"artifacts/effects-probe";
        if(folder==folder.parent_path())break;
    }
    return L"artifacts/effects-probe";
}
static LRESULT CALLBACK windowProc(HWND window,UINT message,WPARAM w,LPARAM l){
    if(message==WM_CLOSE){closing=true;DestroyWindow(window);return 0;}
    if(message==WM_DESTROY){PostQuitMessage(0);return 0;}
    return DefWindowProcW(window,message,w,l);
}
static uint8_t keyboard(){return (GetAsyncKeyState(VK_LEFT)&0x8000?4:0)|(GetAsyncKeyState(VK_RIGHT)&0x8000?8:0)|
    (GetAsyncKeyState(VK_UP)&0x8000?1:0)|(GetAsyncKeyState(VK_DOWN)&0x8000?2:0)|(GetAsyncKeyState(VK_SPACE)&0x8000?128:0);}
int wmain(int argc,wchar_t** argv){
    std::cout<<std::unitbuf;std::cerr<<std::unitbuf;
    std::cout<<"[LLNATIVE] Starting native application; PID "<<GetCurrentProcessId()<<"\n";
    CoInitializeEx(nullptr,COINIT_MULTITHREADED);
    try{
        bool smoke=false,vr=false,control=false,probe=false;
        std::filesystem::path runtime=defaultRuntime(),snapshot=L"artifacts/native-preview.png";
        for(int i=1;i<argc;i++){
            const std::wstring arg=argv[i];if(arg==L"--smoke")smoke=true;else if(arg==L"--vr")vr=true;
            else if(arg==L"--control")control=true;else if(arg==L"--xr-probe")probe=true;
            else if(arg==L"--runtime"&&i+1<argc)runtime=argv[++i];else if(arg==L"--snapshot"&&i+1<argc)snapshot=argv[++i];
            else throw std::runtime_error("Unknown or incomplete native argument");
        }
        std::unique_ptr<NativeXR> xr;if(vr||probe){std::cout<<"[LLNATIVE] Initializing OpenXR\n";xr=std::make_unique<NativeXR>();}if(probe)return 0;
        std::cout<<"[LLNATIVE] Locating original game capture\n";
        Capture capture(runtime);SceneFrame frame;for(int retry=0;retry<100&&!capture.readFrame(frame);retry++)Sleep(10);
        if(frame.sprites.empty())throw std::runtime_error("No completed game frame available");
        std::cout<<"[LLNATIVE] Attached to recorded emulator "<<capture.pid()<<"; original frame "<<frame.sequence<<"; "<<frame.sprites.size()<<" sprite draws; effects="<<frame.hasEffects<<"\n";
        if(smoke){
            Renderer renderer(nullptr,true);renderer.desktop(frame);renderer.save(snapshot);
            std::cout<<"[LLNATIVE] D3D11 software rendering passed; saved "<<snapshot.string()<<"\n";return 0;
        }
        WNDCLASSW type{};type.lpfnWndProc=windowProc;type.hInstance=GetModuleHandleW(nullptr);type.lpszClassName=L"LlamatronNative";type.hCursor=LoadCursorW(nullptr,IDC_ARROW);
        RegisterClassW(&type);HWND window=CreateWindowExW(0,type.lpszClassName,control?L"Llamatron Native":L"Llamatron Native - read-only development preview",
            WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX,CW_USEDEFAULT,CW_USEDEFAULT,1296,839,nullptr,nullptr,type.hInstance,nullptr);
        if(!window)throw std::runtime_error("Cannot create native window");
        std::cout<<"[LLNATIVE] Creating native graphics and sprite resources\n";
        const LUID adapter=xr?xr->adapter():LUID{};Renderer renderer(window,false,xr?&adapter:nullptr);
        if(xr)xr->attach(renderer);ShowWindow(window,SW_SHOW);uint8_t previous=0;
        while(!closing&&(!xr||!xr->quit())){
            MSG message{};while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}
            if(closing)break;capture.readFrame(frame);uint8_t bits=0;
            const bool xrRendered=xr&&xr->frame(renderer,frame,bits);
            if(GetForegroundWindow()==window)bits|=keyboard();
            if(control){const bool fireChanged=((bits^previous)&128)!=0;capture.input(bits,bits&128?0x39:0xb9,fireChanged);previous=bits;}
            renderer.desktop(frame);if(!xrRendered)Sleep(8);
        }
        if(control)capture.input(0,0xb9,true);
        std::cout<<"[LLNATIVE] Closed cleanly\n";
    }catch(const std::exception& error){std::cerr<<"[LLNATIVE] "<<error.what()<<"\n";return 1;}
    CoUninitialize();return 0;
}
