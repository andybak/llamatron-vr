#pragma once
#include "capture.h"
#include <d3d11.h>
#include <dxgi1_2.h>
#include <DirectXMath.h>
#include <wrl/client.h>
#include <map>

using Microsoft::WRL::ComPtr;
struct Vertex {float x,y,z,u,v,shade;};
class Renderer {
public:
    Renderer(HWND window,bool software=false,const LUID* adapterId=nullptr);
    void desktop(const SceneFrame& frame);
    void render(const SceneFrame& frame,ID3D11RenderTargetView* target,unsigned width,unsigned height,
                const DirectX::XMMATRIX& view,const DirectX::XMMATRIX& projection);
    void save(const std::filesystem::path& path);
    ID3D11Device* device() const {return device_.Get();}
    LUID adapterLuid() const;
private:
    struct Graphic {unsigned width{},height{},vertices{};ComPtr<ID3D11ShaderResourceView> texture;ComPtr<ID3D11Buffer> geometry;};
    ComPtr<ID3D11ShaderResourceView> texture(const uint8_t* pixels,unsigned width,unsigned height);
    ComPtr<ID3D11Buffer> geometry(const std::vector<Vertex>& vertices);
    void draw(const Graphic& graphic,const DirectX::XMMATRIX& model,const DirectX::XMMATRIX& viewProjection);
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11DeviceContext> context_;
    ComPtr<IDXGISwapChain> swapchain_;
    ComPtr<ID3D11Texture2D> desktopTexture_,depth_,effects_;
    ComPtr<ID3D11RenderTargetView> desktopTarget_;
    ComPtr<ID3D11DepthStencilView> depthView_;
    ComPtr<ID3D11VertexShader> vertexShader_;
    ComPtr<ID3D11PixelShader> pixelShader_;
    ComPtr<ID3D11InputLayout> layout_;
    ComPtr<ID3D11Buffer> constants_;
    ComPtr<ID3D11SamplerState> sampler_;
    ComPtr<ID3D11RasterizerState> rasterizer_;
    std::map<uint64_t,ComPtr<ID3D11DepthStencilView>> depthSurfaces_;
    std::map<uint32_t,Graphic> graphics_;
    Graphic floor_,effectGraphic_;
    uint16_t publication_{};
    unsigned depthWidth_{},depthHeight_{};
};
