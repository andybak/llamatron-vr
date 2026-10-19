#include "renderer.h"
#include "assets.h"
#include <d3dcompiler.h>
#include <wincodec.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <stdexcept>
#include <iostream>

using namespace DirectX;
static void check(HRESULT result,const char* operation){if(FAILED(result))throw std::runtime_error(operation);}
static const char* shaderSource=R"(
cbuffer Transform:register(b0){float4x4 transform;}
struct V {float3 position:POSITION;float2 uv:TEXCOORD0;float shade:COLOR0;};
struct P {float4 position:SV_POSITION;float2 uv:TEXCOORD0;float shade:COLOR0;};
P vertex(V i){P o;o.position=mul(float4(i.position,1),transform);o.uv=i.uv;o.shade=i.shade;return o;}
Texture2D image:register(t0);SamplerState nearest:register(s0);
float4 pixel(P i):SV_TARGET {float4 c=image.Sample(nearest,i.uv);clip(c.a-.5);return float4(c.rgb*i.shade,c.a);}
)";
static void face(std::vector<Vertex>& vertices,std::array<XMFLOAT3,4> points,float u,float v,float shade){
    for(int i:{0,1,2,0,2,3})vertices.push_back({points[i].x,points[i].y,points[i].z,u,v,shade});
}
static std::vector<Vertex> quad(float z=0){return {
    {-.5f,0,z,0,1,1},{.5f,0,z,1,1,1},{-.5f,1,z,0,0,1},
    {-.5f,1,z,0,0,1},{.5f,0,z,1,1,1},{.5f,1,z,1,0,1}};}
static XMMATRIX model(float x,float y,float z,float width,float height,bool flat=false){
    return XMMATRIX(width,0,0,0,0,flat?0.f:height,flat?-height:0.f,0,0,flat?1.f:0.f,flat?0.f:1.f,0,x,y,z,1);
}
Renderer::Renderer(HWND window,bool software,const LUID* adapterId){
    ComPtr<IDXGIAdapter> adapter;
    if(adapterId){
        ComPtr<IDXGIFactory1> factory;check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)),"Create DXGI factory for shared VR textures");
        for(UINT i=0;factory->EnumAdapters(i,&adapter)!=DXGI_ERROR_NOT_FOUND;i++){
            DXGI_ADAPTER_DESC info{};adapter->GetDesc(&info);
            if(info.AdapterLuid.HighPart==adapterId->HighPart&&info.AdapterLuid.LowPart==adapterId->LowPart)break;
            adapter.Reset();
        }
        if(!adapter)throw std::runtime_error("OpenXR's D3D11 adapter was not found");
    }
    const D3D_FEATURE_LEVEL features[]={D3D_FEATURE_LEVEL_11_0};
    std::cout<<"[LLNATIVE] Creating D3D11 device\n";
    check(D3D11CreateDevice(adapter.Get(),adapter?D3D_DRIVER_TYPE_UNKNOWN:software?D3D_DRIVER_TYPE_WARP:D3D_DRIVER_TYPE_HARDWARE,
        nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,features,1,D3D11_SDK_VERSION,&device_,nullptr,&context_),"Create native D3D11 device");
    std::cout<<"[LLNATIVE] D3D11 device ready\n";
    if(window){
        ComPtr<IDXGIDevice> dxgi;check(device_.As(&dxgi),"Get DXGI device");ComPtr<IDXGIAdapter> own;dxgi->GetAdapter(&own);
        ComPtr<IDXGIFactory> factory;own->GetParent(IID_PPV_ARGS(&factory));
        DXGI_SWAP_CHAIN_DESC desc{};desc.BufferDesc.Width=1280;desc.BufferDesc.Height=800;desc.BufferDesc.Format=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
        desc.SampleDesc.Count=1;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.BufferCount=2;desc.OutputWindow=window;desc.Windowed=TRUE;
        desc.SwapEffect=DXGI_SWAP_EFFECT_DISCARD;check(factory->CreateSwapChain(device_.Get(),&desc,&swapchain_),"Create desktop swapchain");
        check(swapchain_->GetBuffer(0,IID_PPV_ARGS(&desktopTexture_)),"Get desktop surface");
    }else{
        D3D11_TEXTURE2D_DESC desc{};desc.Width=1280;desc.Height=800;desc.MipLevels=desc.ArraySize=1;desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
        desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET;
        check(device_->CreateTexture2D(&desc,nullptr,&desktopTexture_),"Create offscreen desktop surface");
    }
    check(device_->CreateRenderTargetView(desktopTexture_.Get(),nullptr,&desktopTarget_),"Create desktop view");
    ComPtr<ID3DBlob> vs,ps,errors;
    check(D3DCompile(shaderSource,strlen(shaderSource),nullptr,nullptr,nullptr,"vertex","vs_5_0",0,0,&vs,&errors),"Compile native vertex shader");
    check(D3DCompile(shaderSource,strlen(shaderSource),nullptr,nullptr,nullptr,"pixel","ps_5_0",0,0,&ps,&errors),"Compile native pixel shader");
    check(device_->CreateVertexShader(vs->GetBufferPointer(),vs->GetBufferSize(),nullptr,&vertexShader_),"Create vertex shader");
    check(device_->CreatePixelShader(ps->GetBufferPointer(),ps->GetBufferSize(),nullptr,&pixelShader_),"Create pixel shader");
    const D3D11_INPUT_ELEMENT_DESC elements[]={
        {"POSITION",0,DXGI_FORMAT_R32G32B32_FLOAT,0,0,D3D11_INPUT_PER_VERTEX_DATA,0},
        {"TEXCOORD",0,DXGI_FORMAT_R32G32_FLOAT,0,12,D3D11_INPUT_PER_VERTEX_DATA,0},
        {"COLOR",0,DXGI_FORMAT_R32_FLOAT,0,20,D3D11_INPUT_PER_VERTEX_DATA,0}};
    check(device_->CreateInputLayout(elements,3,vs->GetBufferPointer(),vs->GetBufferSize(),&layout_),"Create input layout");
    D3D11_BUFFER_DESC constantDesc{};constantDesc.ByteWidth=64;constantDesc.Usage=D3D11_USAGE_DEFAULT;constantDesc.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
    check(device_->CreateBuffer(&constantDesc,nullptr,&constants_),"Create transformation constants");
    D3D11_SAMPLER_DESC sampling{};sampling.Filter=D3D11_FILTER_MIN_MAG_MIP_POINT;
    sampling.AddressU=sampling.AddressV=sampling.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sampling.MaxLOD=D3D11_FLOAT32_MAX;
    check(device_->CreateSamplerState(&sampling,&sampler_),"Create nearest-pixel sampler");
    D3D11_RASTERIZER_DESC raster{};raster.FillMode=D3D11_FILL_SOLID;raster.CullMode=D3D11_CULL_NONE;raster.DepthClipEnable=TRUE;
    check(device_->CreateRasterizerState(&raster,&rasterizer_),"Create two-sided rasterizer");
    std::cout<<"[LLNATIVE] Building embedded sprite graphics\n";
    for(const auto& asset:embeddedSprites){
        Graphic graphic;graphic.width=asset.width;graphic.height=asset.height;graphic.texture=texture(asset.rgba,asset.width,asset.height);
        auto vertices=quad(.006f);const auto back=quad(-.006f);vertices.insert(vertices.end(),back.begin(),back.end());
        const auto opaque=[&](int x,int y){return x>=0&&y>=0&&x<asset.width&&y<asset.height&&asset.rgba[(y*asset.width+x)*4+3]>=128;};
        for(int y=0;y<asset.height;y++)for(int x=0;x<asset.width;x++){
            if(!opaque(x,y))continue;
            const float l=float(x)/asset.width-.5f,r=float(x+1)/asset.width-.5f,t=1-float(y)/asset.height,b=1-float(y+1)/asset.height;
            const float u=(x+.5f)/asset.width,v=(y+.5f)/asset.height,d=.006f;
            if(!opaque(x-1,y))face(vertices,{{{l,b,-d},{l,b,d},{l,t,d},{l,t,-d}}},u,v,.62f);
            if(!opaque(x+1,y))face(vertices,{{{r,b,d},{r,b,-d},{r,t,-d},{r,t,d}}},u,v,.62f);
            if(!opaque(x,y-1))face(vertices,{{{l,t,d},{r,t,d},{r,t,-d},{l,t,-d}}},u,v,.62f);
            if(!opaque(x,y+1))face(vertices,{{{l,b,-d},{r,b,-d},{r,b,d},{l,b,d}}},u,v,.62f);
        }
        graphic.vertices=static_cast<unsigned>(vertices.size());graphic.geometry=geometry(vertices);
        graphics_.emplace((uint32_t(asset.bank)<<16)|asset.offset,std::move(graphic));
    }
    std::vector<uint8_t> grid(320*200*4);
    for(int y=0;y<200;y++)for(int x=0;x<320;x++){
        const bool border=x<2||y<2||x>317||y>197,line=x%16==0||y%16==0;const size_t p=(y*320+x)*4;
        grid[p]=border?112:line?29:16;grid[p+1]=border?183:line?55:33;grid[p+2]=border?164:line?60:38;grid[p+3]=255;
    }
    floor_.texture=texture(grid.data(),320,200);floor_.geometry=geometry(quad());floor_.vertices=6;
    D3D11_TEXTURE2D_DESC effectDesc{};effectDesc.Width=320;effectDesc.Height=200;effectDesc.MipLevels=effectDesc.ArraySize=1;
    effectDesc.Format=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;effectDesc.SampleDesc.Count=1;effectDesc.BindFlags=D3D11_BIND_SHADER_RESOURCE;
    check(device_->CreateTexture2D(&effectDesc,nullptr,&effects_),"Create original pixel-effect texture");
    check(device_->CreateShaderResourceView(effects_.Get(),nullptr,&effectGraphic_.texture),"Create effect view");
    effectGraphic_.geometry=floor_.geometry;effectGraphic_.vertices=6;
    std::cout<<"[LLNATIVE] Native renderer ready\n";
}
ComPtr<ID3D11ShaderResourceView> Renderer::texture(const uint8_t* pixels,unsigned width,unsigned height){
    D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.MipLevels=desc.ArraySize=1;desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
    desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_IMMUTABLE;desc.BindFlags=D3D11_BIND_SHADER_RESOURCE;
    D3D11_SUBRESOURCE_DATA data{pixels,width*4,0};ComPtr<ID3D11Texture2D> image;
    check(device_->CreateTexture2D(&desc,&data,&image),"Create original sprite texture");ComPtr<ID3D11ShaderResourceView> view;
    check(device_->CreateShaderResourceView(image.Get(),nullptr,&view),"Create original sprite view");return view;
}
ComPtr<ID3D11Buffer> Renderer::geometry(const std::vector<Vertex>& vertices){
    D3D11_BUFFER_DESC desc{};desc.ByteWidth=static_cast<UINT>(vertices.size()*sizeof(Vertex));desc.Usage=D3D11_USAGE_IMMUTABLE;desc.BindFlags=D3D11_BIND_VERTEX_BUFFER;
    D3D11_SUBRESOURCE_DATA data{vertices.data(),0,0};ComPtr<ID3D11Buffer> buffer;
    check(device_->CreateBuffer(&desc,&data,&buffer),"Create sprite extrusion geometry");return buffer;
}
void Renderer::draw(const Graphic& graphic,const XMMATRIX& transform,const XMMATRIX& viewProjection){
    XMFLOAT4X4 matrix;XMStoreFloat4x4(&matrix,XMMatrixTranspose(transform*viewProjection));
    context_->UpdateSubresource(constants_.Get(),0,nullptr,&matrix,0,0);ID3D11Buffer* buffer=graphic.geometry.Get();UINT stride=sizeof(Vertex),offset=0;
    context_->IASetVertexBuffers(0,1,&buffer,&stride,&offset);ID3D11ShaderResourceView* image=graphic.texture.Get();context_->PSSetShaderResources(0,1,&image);
    context_->Draw(graphic.vertices,0);
}
void Renderer::render(const SceneFrame& frame,ID3D11RenderTargetView* target,unsigned width,unsigned height,const XMMATRIX& view,const XMMATRIX& projection){
    if(width!=depthWidth_||height!=depthHeight_){
        const uint64_t key=(uint64_t(width)<<32)|height;
        auto found=depthSurfaces_.find(key);
        if(found==depthSurfaces_.end()){
            ComPtr<ID3D11Texture2D> depth;ComPtr<ID3D11DepthStencilView> depthView;
            D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.MipLevels=desc.ArraySize=1;
            desc.Format=DXGI_FORMAT_D24_UNORM_S8_UINT;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_DEPTH_STENCIL;
            check(device_->CreateTexture2D(&desc,nullptr,&depth),"Create depth surface");
            check(device_->CreateDepthStencilView(depth.Get(),nullptr,&depthView),"Create depth view");found=depthSurfaces_.emplace(key,std::move(depthView)).first;
        }
        depthView_=found->second;depthWidth_=width;depthHeight_=height;
    }
    if(frame.hasEffects&&frame.publication!=publication_){
        ID3D11ShaderResourceView* none=nullptr;context_->PSSetShaderResources(0,1,&none);
        context_->UpdateSubresource(effects_.Get(),0,nullptr,frame.effects.data(),320*4,0);publication_=frame.publication;
    }
    const auto linear=[](float srgb){return srgb<=.04045f?srgb/12.92f:std::pow((srgb+.055f)/1.055f,2.4f);};
    const float background[]={linear(.025f),linear(.035f),linear(.055f),1};context_->OMSetRenderTargets(1,&target,depthView_.Get());
    context_->ClearRenderTargetView(target,background);context_->ClearDepthStencilView(depthView_.Get(),D3D11_CLEAR_DEPTH,1,0);
    D3D11_VIEWPORT viewport{0,0,float(width),float(height),0,1};context_->RSSetViewports(1,&viewport);context_->RSSetState(rasterizer_.Get());
    context_->IASetInputLayout(layout_.Get());context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context_->VSSetShader(vertexShader_.Get(),nullptr,0);context_->PSSetShader(pixelShader_.Get(),nullptr,0);
    ID3D11Buffer* constants=constants_.Get();context_->VSSetConstantBuffers(0,1,&constants);ID3D11SamplerState* sampler=sampler_.Get();context_->PSSetSamplers(0,1,&sampler);
    const XMMATRIX vp=view*projection;draw(floor_,model(0,.65f,-1,2,1.5f,true),vp);
    if(frame.hasEffects)draw(effectGraphic_,model(0,.72f,-1,2,1.5f,true),vp);
    for(const auto& sprite:frame.sprites){
        if(frame.hasEffects&&sprite.nativeEffect)continue;
        const auto found=graphics_.find((uint32_t(sprite.bank)<<16)|sprite.offset);if(found==graphics_.end())continue;const auto& graphic=found->second;
        const float spriteWidth=graphic.width*.00625f,spriteHeight=graphic.height*.0075f,x=(sprite.x-160)*.00625f,z=-2.5f+sprite.y*.0075f;
        const bool shot=sprite.bank==0x118c&&(sprite.offset==0x1a12||sprite.offset==0x1a3c||sprite.offset==0x1ac0||sprite.offset==0x1b44||
            sprite.offset==0x1bc8||sprite.offset==0x1bf2||sprite.offset==0x1c76||sprite.offset==0x1cfa);
        draw(graphic,sprite.bank==0x20eb?model(x,1.38f-sprite.y*.003f,-2.53f,spriteWidth,spriteHeight):
            model(x,shot?.72f:.658f,shot?z+spriteHeight/2:z,spriteWidth,spriteHeight,shot),vp);
    }
}
void Renderer::desktop(const SceneFrame& frame){
    render(frame,desktopTarget_.Get(),1280,800,XMMatrixLookAtRH(XMVectorSet(.7f,2.5f,.6f,1),XMVectorSet(0,.65f,-1.75f,1),XMVectorSet(0,1,0,0)),
        XMMatrixPerspectiveFovRH(XM_PI/3,1280.f/800,.02f,50));
    if(swapchain_)swapchain_->Present(0,0);
}
LUID Renderer::adapterLuid() const{
    ComPtr<IDXGIDevice> dxgi;check(device_.As(&dxgi),"Get graphics adapter");ComPtr<IDXGIAdapter> adapter;dxgi->GetAdapter(&adapter);
    DXGI_ADAPTER_DESC desc{};adapter->GetDesc(&desc);return desc.AdapterLuid;
}
void Renderer::save(const std::filesystem::path& path){
    D3D11_TEXTURE2D_DESC desc{};desktopTexture_->GetDesc(&desc);desc.Usage=D3D11_USAGE_STAGING;desc.BindFlags=0;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    ComPtr<ID3D11Texture2D> staging;check(device_->CreateTexture2D(&desc,nullptr,&staging),"Create readback surface");context_->CopyResource(staging.Get(),desktopTexture_.Get());
    D3D11_MAPPED_SUBRESOURCE mapped{};check(context_->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped),"Read native frame");
    std::vector<uint8_t> pixels(desc.Width*desc.Height*4);for(unsigned y=0;y<desc.Height;y++)memcpy(pixels.data()+y*desc.Width*4,static_cast<uint8_t*>(mapped.pData)+y*mapped.RowPitch,desc.Width*4);
    context_->Unmap(staging.Get(),0);ComPtr<IWICImagingFactory> factory;check(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&factory)),"Create PNG encoder");
    ComPtr<IWICStream> stream;factory->CreateStream(&stream);check(stream->InitializeFromFilename(path.c_str(),GENERIC_WRITE),"Open native screenshot");
    ComPtr<IWICBitmapEncoder> encoder;factory->CreateEncoder(GUID_ContainerFormatPng,nullptr,&encoder);encoder->Initialize(stream.Get(),WICBitmapEncoderNoCache);
    ComPtr<IWICBitmapFrameEncode> image;encoder->CreateNewFrame(&image,nullptr);image->Initialize(nullptr);image->SetSize(desc.Width,desc.Height);WICPixelFormatGUID format=GUID_WICPixelFormat32bppBGRA;
    check(image->SetPixelFormat(&format),"Select screenshot pixel format");
    if(format!=GUID_WICPixelFormat32bppBGRA)throw std::runtime_error("PNG encoder changed the requested pixel format");
    for(size_t pixel=0;pixel<pixels.size();pixel+=4)std::swap(pixels[pixel],pixels[pixel+2]);
    check(image->WritePixels(desc.Height,desc.Width*4,static_cast<UINT>(pixels.size()),pixels.data()),"Write native screenshot");image->Commit();encoder->Commit();
}
