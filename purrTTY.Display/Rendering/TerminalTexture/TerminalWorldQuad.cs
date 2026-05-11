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
    private readonly string _assetPrefix = $"purrTTY/TerminalWorldQuad/{Guid.NewGuid():N}";
    private MeshBucketHandle _meshBucket;
    private int _materialHandle = -1;
    private int? _materialBindlessHandle;
    private int _materialGeneration;
    private bool _registered;
    private bool _loggedSubmission;
    private string? _lastError;

    public TerminalWorldQuad(TerminalRenderServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public void Draw(TerminalRenderTexture texture, float textureWidth, float textureHeight)
    {
        try
        {
            var meshRenderSystem = _services.MeshRenderSystem;
            var meshBucketSystem = meshRenderSystem?.MeshBucketSystem;
            if (meshRenderSystem == null || meshBucketSystem == null)
            {
                LogOnce("purrTTY world quad unavailable: KSA mesh bucket system is not ready");
                return;
            }

            if (!EnsureRegistered(texture, meshRenderSystem, meshBucketSystem))
            {
                return;
            }

            var aspect = MathF.Max(0.1f, textureWidth / MathF.Max(1f, textureHeight));
            var instance = new InstanceData
            {
                model = CreateCameraFacingTransform(aspect),
                data = new float4(_materialHandle, 0f, 0f, 0f)
            };

            meshBucketSystem.DrawMeshInstance(_meshBucket, instance);
            LogSubmissionOnce(aspect);
        }
        catch (Exception ex)
        {
            LogOnce($"purrTTY world quad draw failed: {ex}");
        }
    }

    public void Dispose()
    {
        var meshBucketSystem = _services.MeshRenderSystem?.MeshBucketSystem;
        if (_registered && meshBucketSystem != null)
        {
            meshBucketSystem.UnregisterMesh(_meshBucket);
        }

        _registered = false;

        if (_materialHandle >= 0 && _services.MaterialSystem != null)
        {
            _services.MaterialSystem.Free(_materialHandle);
        }

        _materialHandle = -1;
        _materialBindlessHandle = null;
    }

    private bool EnsureRegistered(TerminalRenderTexture texture, SuperMeshRenderSystem meshRenderSystem, MeshBucketSystem<InstanceData> meshBucketSystem)
    {
        if (meshRenderSystem.MeshRendererStaticPbr == null || meshRenderSystem.MeshIndirectSystem == null)
        {
            LogOnce("purrTTY world quad unavailable: KSA mesh render systems are not ready");
            return false;
        }

        var textureSystem = _services.TextureSystem;
        if (textureSystem == null || textureSystem.DefaultWhiteTexture == null)
        {
            LogOnce("purrTTY world quad unavailable: KSA texture system is not ready");
            return false;
        }

        var materialSystem = _services.MaterialSystem;
        if (materialSystem == null)
        {
            LogOnce("purrTTY world quad unavailable: KSA material system is not ready");
            return false;
        }

        if (!texture.EnsureBindlessTexture() || !texture.BindlessTextureHandle.HasValue)
        {
            return false;
        }

        if (!_registered)
        {
            var mesh = CreateOrLoadQuadMesh(meshRenderSystem);
            _meshBucket = meshBucketSystem.RegisterMesh(
                mesh,
                meshRenderSystem.MeshRendererStaticPbr);
            _registered = true;
        }

        int bindlessHandle = texture.BindlessTextureHandle.Value;
        if (_materialHandle < 0 || _materialBindlessHandle != bindlessHandle)
        {
            if (_materialHandle >= 0)
            {
                materialSystem.Free(_materialHandle);
            }

            var materialName = new AssetName($"{_assetPrefix}/Material/{++_materialGeneration}");
            bool created = materialSystem.CreateObject(materialName, new MaterialData
            {
                AlbedoTexture = bindlessHandle,
                NormalTexture = meshRenderSystem.GltfSystem.BlankNormalTexture.BindlessHandle,
                RoughMetallicAOTexture = meshRenderSystem.GltfSystem.BlankMaterialTexture.BindlessHandle,
                Sampler = textureSystem.SamplerClampHandle,
                AlbedoColor = float4.One,
                RoughnessMetalScale = float4.One,
                ExtraData = float4.Zero,
                EmissiveTexture = textureSystem.DefaultBlackTexture.BindlessHandle
            });

            if (!created)
            {
                LogOnce($"purrTTY world quad unavailable: failed to create material object {materialName}");
                return false;
            }

            _materialHandle = materialSystem.GetOrLoad(materialName).Handle;
            _materialBindlessHandle = bindlessHandle;
        }

        return true;
    }

    private MeshIndirectRef CreateOrLoadQuadMesh(SuperMeshRenderSystem meshRenderSystem)
    {
        var meshName = new AssetName("purrTTY.Terminal.WorldQuad.Mesh");
        if (meshRenderSystem.MeshIndirectSystem.IsLoaded(meshName))
        {
            return meshRenderSystem.MeshIndirectSystem.GetOrLoad(meshName);
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

        if (!meshRenderSystem.MeshIndirectSystem.AddMesh(meshName, mesh) &&
            !meshRenderSystem.MeshIndirectSystem.IsLoaded(meshName))
        {
            throw new InvalidOperationException("Failed to register terminal world quad mesh");
        }

        return meshRenderSystem.MeshIndirectSystem.GetOrLoad(meshName);
    }

    private float4x4 CreateCameraFacingTransform(float aspect)
    {
        try
        {
            var camera = Program.GetMainCamera();
            if (camera == null)
            {
                LogOnce("purrTTY world quad using fallback transform: KSA main camera is null");
                return CreateFallbackTransform(aspect);
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
        catch (Exception ex)
        {
            LogOnce($"purrTTY world quad using fallback transform: failed to read KSA main camera: {ex.Message}");
            return CreateFallbackTransform(aspect);
        }
    }

    private static float4x4 CreateFallbackTransform(float aspect)
    {
        var fallbackHeight = DefaultHeightMeters;
        var fallbackWidth = fallbackHeight * aspect;
        return new float4x4(
            fallbackWidth, 0f, 0f, 0f,
            0f, fallbackHeight, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, DefaultDistanceMeters, 1f);
    }

    private void LogSubmissionOnce(float aspect)
    {
        if (_loggedSubmission)
        {
            return;
        }

        _loggedSubmission = true;
        try
        {
            var camera = Program.GetMainCamera();
            var center = camera.GetForward() * DefaultDistanceMeters;
            var screen = camera.EgoToScreen(center, ignoreBehind: false);
            ModLog.Log.Debug($"purrTTY world quad submitted: screen=({screen.X:0.0},{screen.Y:0.0}) distance={DefaultDistanceMeters:0.0}m aspect={aspect:0.00} material={_materialHandle}");
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY world quad submitted: distance={DefaultDistanceMeters:0.0}m aspect={aspect:0.00} material={_materialHandle}; screen diagnostic failed: {ex.Message}");
        }
    }

    private void LogOnce(string message)
    {
        if (_lastError == message)
        {
            return;
        }

        _lastError = message;
        ModLog.Log.Debug(message);
    }
}
