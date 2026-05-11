using Brutal.Numerics;
using Brutal.Render.Mesh;
using Core;
using KSA;
using purrTTY.Logging;
using RenderCore;
using RenderCore.Systems;

namespace purrTTY.Display.Rendering.TerminalTexture;

internal sealed class TerminalWorldQuad : IDisposable
{
    private const float DefaultHeightMeters = 1.2f;
    private const float DefaultDistanceMeters = 4f;
    private readonly TerminalRenderServices _services;
    private MeshBucketHandle _meshBucket;
    private int _materialHandle = -1;
    private int? _materialBindlessHandle;
    private int _materialGeneration;
    private bool _registered;
    private string? _lastError;

    public TerminalWorldQuad(TerminalRenderServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public void Draw(TerminalRenderTexture texture, float textureWidth, float textureHeight)
    {
        try
        {
            if (!EnsureRegistered(texture))
            {
                return;
            }

            var aspect = MathF.Max(0.1f, textureWidth / MathF.Max(1f, textureHeight));
            var instance = new InstanceData
            {
                model = CreateCameraFacingTransform(aspect),
                data = new float4(_materialHandle, 0f, 0f, 0f)
            };

            _services.MeshRenderSystem.MeshBucketSystem.DrawMeshInstance(_meshBucket, instance);
        }
        catch (Exception ex)
        {
            if (_lastError != ex.Message)
            {
                _lastError = ex.Message;
                ModLog.Log.Debug($"purrTTY world quad draw failed: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            _services.MeshRenderSystem.MeshBucketSystem.UnregisterMesh(_meshBucket);
            _registered = false;
        }

        if (_materialHandle >= 0)
        {
            _services.MaterialSystem.Free(_materialHandle);
            _materialHandle = -1;
            _materialBindlessHandle = null;
        }
    }

    private bool EnsureRegistered(TerminalRenderTexture texture)
    {
        if (_services.MeshRenderSystem.MeshBucketSystem == null || _services.MeshRenderSystem.MeshRendererStaticPbr == null)
        {
            ModLog.Log.Debug("purrTTY world quad unavailable: KSA mesh render systems are not ready");
            return false;
        }

        if (_services.TextureSystem.DefaultWhiteTexture == null)
        {
            ModLog.Log.Debug("purrTTY world quad unavailable: KSA default white texture is not ready");
            return false;
        }

        if (!texture.EnsureBindlessTexture() || !texture.BindlessTextureHandle.HasValue)
        {
            return false;
        }

        if (!_registered)
        {
            var mesh = CreateOrLoadQuadMesh();
            _meshBucket = _services.MeshRenderSystem.MeshBucketSystem.RegisterMesh(
                mesh,
                _services.MeshRenderSystem.MeshRendererStaticPbr);
            _registered = true;
        }

        int bindlessHandle = texture.BindlessTextureHandle.Value;
        if (_materialHandle < 0 || _materialBindlessHandle != bindlessHandle)
        {
            if (_materialHandle >= 0)
            {
                _services.MaterialSystem.Free(_materialHandle);
            }

            var materialName = new AssetName($"purrTTY.Terminal.WorldQuad.Material.{++_materialGeneration}");
            bool created = _services.MaterialSystem.CreateObject(materialName, new MaterialData
            {
                AlbedoTexture = bindlessHandle,
                NormalTexture = _services.TextureSystem.DefaultWhiteTexture.BindlessHandle,
                RoughMetallicAOTexture = _services.TextureSystem.DefaultWhiteTexture.BindlessHandle,
                Sampler = _services.TextureSystem.SamplerClampHandle,
                AlbedoColor = float4.One,
                RoughnessMetalScale = new float4(1f, 0f, 1f, 1f),
                ExtraData = float4.Zero,
                EmissiveTexture = bindlessHandle
            });

            if (!created)
            {
                ModLog.Log.Debug("purrTTY world quad unavailable: failed to create material object");
                return false;
            }

            _materialHandle = _services.MaterialSystem.GetOrLoad(materialName).Handle;
            _materialBindlessHandle = bindlessHandle;
        }

        return true;
    }

    private MeshIndirectRef CreateOrLoadQuadMesh()
    {
        var meshName = new AssetName("purrTTY.Terminal.WorldQuad.Mesh");
        if (_services.MeshRenderSystem.MeshIndirectSystem.IsLoaded(meshName))
        {
            return _services.MeshRenderSystem.MeshIndirectSystem.GetOrLoad(meshName);
        }

        using var mesh = new MeshAsset
        {
            PositionMinimum = new double3(-0.5, -0.5, 0.0),
            PositionMaximum = new double3(0.5, 0.5, 0.0)
        };

        Span<float3> positions = stackalloc float3[]
        {
            new(-0.5f, 0.5f, 0f),
            new(0.5f, 0.5f, 0f),
            new(0.5f, -0.5f, 0f),
            new(-0.5f, -0.5f, 0f),
            new(-0.5f, 0.5f, 0f),
            new(0.5f, 0.5f, 0f),
            new(0.5f, -0.5f, 0f),
            new(-0.5f, -0.5f, 0f)
        };
        Span<InterleavedVertex> vertices = stackalloc InterleavedVertex[]
        {
            new() { Normal = new float3(0f, 0f, -1f), Uv0 = new float2(0f, 0f) },
            new() { Normal = new float3(0f, 0f, -1f), Uv0 = new float2(1f, 0f) },
            new() { Normal = new float3(0f, 0f, -1f), Uv0 = new float2(1f, 1f) },
            new() { Normal = new float3(0f, 0f, -1f), Uv0 = new float2(0f, 1f) },
            new() { Normal = new float3(0f, 0f, 1f), Uv0 = new float2(0f, 0f) },
            new() { Normal = new float3(0f, 0f, 1f), Uv0 = new float2(1f, 0f) },
            new() { Normal = new float3(0f, 0f, 1f), Uv0 = new float2(1f, 1f) },
            new() { Normal = new float3(0f, 0f, 1f), Uv0 = new float2(0f, 1f) }
        };
        Span<int> indices = stackalloc int[]
        {
            0, 1, 2,
            0, 2, 3,
            6, 5, 4,
            7, 6, 4
        };

        mesh.SetVerticesFromData(MeshAttribute.Position, positions);
        mesh.SetVerticesFromData(MeshAttribute.Interleaved, vertices);
        mesh.SetIndicesFromData(indices);
        mesh.Update();

        if (!_services.MeshRenderSystem.MeshIndirectSystem.AddMesh(meshName, mesh) &&
            !_services.MeshRenderSystem.MeshIndirectSystem.IsLoaded(meshName))
        {
            throw new InvalidOperationException("Failed to register terminal world quad mesh");
        }

        return _services.MeshRenderSystem.MeshIndirectSystem.GetOrLoad(meshName);
    }

    private static float4x4 CreateCameraFacingTransform(float aspect)
    {
        var camera = Program.GetMainCamera();
        if (camera == null)
        {
            var fallbackHeight = DefaultHeightMeters;
            var fallbackWidth = fallbackHeight * aspect;
            return new float4x4(
                fallbackWidth, 0f, 0f, 0f,
                0f, fallbackHeight, 0f, 0f,
                0f, 0f, 1f, 0f,
                0f, 0f, DefaultDistanceMeters, 1f);
        }

        var forward = float3.Normalize(float3.Pack(camera.GetForward()));
        var right = float3.Normalize(float3.Pack(camera.GetRight()));
        var up = float3.Normalize(float3.Pack(camera.GetUp()));
        var height = DefaultHeightMeters;
        var width = height * aspect;
        var center = forward * DefaultDistanceMeters;

        return new float4x4(
            right.X * width, right.Y * width, right.Z * width, 0f,
            up.X * height, up.Y * height, up.Z * height, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            center.X, center.Y, center.Z, 1f);
    }
}