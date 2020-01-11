﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
public class RayTracingSystem
{
    public ComputeShader RayTracingComputeShader;
    int? kernalIndex = null;
    int KERNALINDEX
    {
        get
        {
            if (kernalIndex == null)
            {
                kernalIndex = RayTracingComputeShader.FindKernel("CSMain");
            }
            return (int) kernalIndex;
        }
    }
    public int samplePerPixel;
    public int depth;
    public Vector2Int resolution = new Vector2Int(1280, 720);
    public Texture2D skyBoxTexture;
    RenderTexture renderTexture;
    RenderTexture GetRenderTexture
    {
        get
        {
            if (renderTexture == null)
            {
                renderTexture = RenderTexture.GetTemporary(resolution.x, resolution.y, 0, RenderTextureFormat.DefaultHDR);
                renderTexture.enableRandomWrite = true;
                renderTexture.autoGenerateMips = false;
                renderTexture.Create();
            }
            return renderTexture;
        }
    }

    RenderTexture renderTextureOut;
    RenderTexture GetRenderTextureOut
    {
        get
        {
            if (renderTextureOut == null)
            {
                renderTextureOut = RenderTexture.GetTemporary(resolution.x, resolution.y, 0, RenderTextureFormat.DefaultHDR);
                renderTextureOut.enableRandomWrite = true;
                renderTextureOut.autoGenerateMips = false;
                renderTextureOut.Create();
            }
            return renderTextureOut;
        }
    }

    public int needReset = 0;
    public void ResetRenderTexture()
    {
        if (renderTexture != null) renderTexture.Release();
        renderTexture = null;

        if (renderTextureOut != null) renderTextureOut.Release();
        renderTextureOut = null;

        layerCount = 0;
    }
    public Camera camera;
    public int layerCount = 0;
    //Scene
    //List<RayTracingEntity> RayTracingEntities = new List<RayTracingEntity>();
    public RayTracingEntity[] RayTracingEntities;

    public void Release()
    {
        if (renderTexture)
        {
            renderTexture.Release();
            renderTexture = null;
        }

        if (sphereBuffer != null)
            sphereBuffer.Release();

        if (boxBuffer != null)
            boxBuffer.Release();

        if (_meshObjectBuffer != null)
            _meshObjectBuffer.Release();
        if (_vertexBuffer != null)
            _vertexBuffer.Release();
        if (_indexBuffer != null)
            _indexBuffer.Release();
    }

    void SetupScene()
    {
        SetupSpheres();
        SetupBoxs();
        RebuildMeshObjectBuffers();
    }
    Vector3 ColorToVector3(Color c)
    {
        return new Vector3(c.r, c.g, c.b);
    }
    //Sphere
    public struct SphereInfo
    {
        public Vector3 position;
        public Vector3 rotation;
        public float radius;
        public Vector3 albedo;
        public Vector3 specular;
        public float smoothness;
        public Vector3 emission;
    };
    ComputeBuffer sphereBuffer;
    public List<SphereInfo> spheres = new List<SphereInfo>();
    void SetupSpheres()
    {
        spheres.Clear();
        foreach (var item in RayTracingEntities)
        {
            if (item.entityType == RayTracingEntity.EntityType.Sphere && item.gameObject.activeSelf == true)
            {
                var sphere = new SphereInfo();
                sphere.position = item.transform.position;
                sphere.rotation = item.transform.rotation.eulerAngles;
                sphere.radius = item.radius / 2.0f;
                sphere.albedo = ColorToVector3(item.albedo);
                sphere.specular = ColorToVector3(item.specular);
                sphere.smoothness = item.smoothness;
                sphere.emission = ColorToVector3(item.emission);
                spheres.Add(sphere);
            }
        }
        if (sphereBuffer != null)
            sphereBuffer.Release();
        // Assign to compute buffer
        int stride;
        unsafe
        {
            stride = sizeof(SphereInfo);
        }
        sphereBuffer = new ComputeBuffer(spheres.Count == 0 ? CreateEmtpySpheres() : spheres.Count, stride);
        sphereBuffer.SetData(spheres);
    }
    int CreateEmtpySpheres()
    {
        spheres.Add(new SphereInfo());
        return spheres.Count;
    }

    //Box
    public struct BoxInfo
    {
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 size;
        public Vector3 albedo;
        public Vector3 specular;
        public float smoothness;
        public Vector3 emission;
    };
    ComputeBuffer boxBuffer;
    public List<BoxInfo> boxs = new List<BoxInfo>();
    void SetupBoxs()
    {
        boxs.Clear();
        foreach (var item in RayTracingEntities)
        {
            if (item.entityType == RayTracingEntity.EntityType.Box && item.gameObject.activeSelf == true)
            {
                var box = new BoxInfo();
                box.position = item.transform.position;
                box.rotation = item.transform.rotation.eulerAngles;
                box.size = item.boxSize;
                box.albedo = ColorToVector3(item.albedo);
                box.specular = ColorToVector3(item.specular);
                box.smoothness = item.smoothness;
                box.emission = ColorToVector3(item.emission);
                boxs.Add(box);
            }
        }
        if (boxBuffer != null)
            boxBuffer.Release();
        // Assign to compute buffer
        int stride;
        unsafe
        {
            stride = sizeof(BoxInfo);
        }
        boxBuffer = new ComputeBuffer(boxs.Count == 0 ? CreateEmtpyBoxs() : boxs.Count, stride);
        boxBuffer.SetData(boxs);
    }
    int CreateEmtpyBoxs()
    {
        boxs.Add(new BoxInfo());
        return boxs.Count;
    }

    //mesh 
    struct MeshObject
    {
        public Matrix4x4 localToWorldMatrix;
        public int indices_offset;
        public int indices_count;

        public Vector3 albedo;
        public Vector3 specular;
        public float smoothness;
        public Vector3 emission;
    };

    private List<MeshObject> _meshObjects = new List<MeshObject>();
    private List<Vector3> _vertices = new List<Vector3>();
    private List<int> _indices = new List<int>();
    private ComputeBuffer _meshObjectBuffer;
    private ComputeBuffer _vertexBuffer;
    private ComputeBuffer _indexBuffer;
    private void RebuildMeshObjectBuffers()
    {
        // Clear all lists
        _meshObjects.Clear();
        _vertices.Clear();
        _indices.Clear();

        foreach (RayTracingEntity item in RayTracingEntities)
        {
            if (item.entityType == RayTracingEntity.EntityType.Mesh && item.gameObject.activeSelf == true)
            {
                MeshFilter meshFilter = item.gameObject.GetComponent<MeshFilter>();
                if (meshFilter == null) continue;

                Mesh mesh = meshFilter.sharedMesh;
                if (mesh == null) continue;

                // Add vertex data
                int firstVertex = _vertices.Count;
                _vertices.AddRange(mesh.vertices);

                // Add index data - if the vertex buffer wasn't empty before, the
                // indices need to be offset
                int firstIndex = _indices.Count;
                var indices = mesh.GetIndices(0);
                _indices.AddRange(indices.Select(index => index + firstVertex));

                // Add the object itself
                _meshObjects.Add(new MeshObject()
                {
                    localToWorldMatrix = item.transform.localToWorldMatrix,
                        indices_offset = firstIndex,
                        indices_count = indices.Length,

                        albedo = ColorToVector3(item.albedo),
                        specular = ColorToVector3(item.specular),
                        smoothness = item.smoothness,
                        emission = ColorToVector3(item.emission),
                });
            }
        }
        unsafe
        {
            CreateComputeBuffer(ref _meshObjectBuffer, _meshObjects, sizeof(MeshObject));
            CreateComputeBuffer(ref _vertexBuffer, _vertices, sizeof(Vector3));
            CreateComputeBuffer(ref _indexBuffer, _indices, sizeof(int));
        }
    }
    void CreateComputeBuffer<T>(ref ComputeBuffer buffer, List<T> data, int stride)
    where T : struct
    {
        // Do we already have a compute buffer?
        if (buffer != null)
        {
            // If no data or buffer doesn't match the given criteria, release it
            if (data.Count == 0 || buffer.count != data.Count || buffer.stride != stride)
            {
                buffer.Release();
                buffer = null;
            }
        }

        if (data.Count != 0)
        {
            // If the buffer has been released or wasn't there to
            // begin with, create it
            if (buffer == null)
            {
                buffer = new ComputeBuffer(data.Count, stride);
            }

            // Set data on the buffer
            buffer.SetData(data);
        }
    }

    void SetShaderParameters()
    {
        RayTracingComputeShader.SetMatrix("_CameraToWorld", camera.cameraToWorldMatrix);
        RayTracingComputeShader.SetMatrix("_CameraInverseProjection", camera.projectionMatrix.inverse);

        RayTracingComputeShader.SetTexture(KERNALINDEX, "_SkyboxTexture", skyBoxTexture);

        RayTracingComputeShader.SetVector("_PixelOffset", new Vector2(UnityEngine.Random.value, UnityEngine.Random.value));

        RayTracingComputeShader.SetVector("_ScreenSize", new Vector2(GetRenderTexture.width, GetRenderTexture.height));

        RayTracingComputeShader.SetInt("_depth", depth);
        RayTracingComputeShader.SetInt("_SamplePerPixel", samplePerPixel);
        RayTracingComputeShader.SetFloat("_Seed", UnityEngine.Random.value);
        RayTracingComputeShader.SetInt("_layerCount", layerCount);

        //Sphere
        RayTracingComputeShader.SetBuffer(KERNALINDEX, "_Spheres", sphereBuffer);
        RayTracingComputeShader.SetInt("_numSpheres", sphereBuffer.count);

        //Boxs
        RayTracingComputeShader.SetBuffer(KERNALINDEX, "_Boxs", boxBuffer);
        RayTracingComputeShader.SetInt("_numBoxs", boxBuffer.count);

        //mesh
        SetComputeBuffer("_MeshObjects", _meshObjectBuffer);
        SetComputeBuffer("_Vertices", _vertexBuffer);
        SetComputeBuffer("_Indices", _indexBuffer);

        RayTracingComputeShader.SetInt("_numMeshObjects", _meshObjectBuffer.count);
        RayTracingComputeShader.SetFloat("_CameraFar", camera.farClipPlane);
    }

    private void SetComputeBuffer(string name, ComputeBuffer buffer)
    {
        if (buffer != null)
        {
            RayTracingComputeShader.SetBuffer(KERNALINDEX, name, buffer);
        }
    }

    public RenderTexture Render()
    {
        RayTracingEntities = GameObject.FindObjectsOfType<RayTracingEntity>();

        foreach (var item in RayTracingEntities)
        {
            if (item.transform.hasChanged)
            {
                needReset += 1;
                item.transform.hasChanged = false;
            }
        }
        if (needReset > 0) { ResetRenderTexture(); needReset = 0; }

        SetupScene();

        SetShaderParameters();

        Dispatch();
        return renderTextureOut;
    }

    void Dispatch()
    {
        RayTracingComputeShader.SetTexture(KERNALINDEX, "Result", GetRenderTexture);
        RayTracingComputeShader.SetTexture(KERNALINDEX, "ResultOut", GetRenderTextureOut);
        int threadGroupsX = Mathf.CeilToInt(GetRenderTexture.width / 8);
        int threadGroupsY = Mathf.CeilToInt(GetRenderTexture.height / 8);
        RayTracingComputeShader.Dispatch(KERNALINDEX, threadGroupsX, threadGroupsY, 1);
        layerCount++;
    }

}