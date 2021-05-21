using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Tiny;
using Unity.Tiny.Input;
using Unity.Tiny.Rendering;
using Unity.Tiny.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System;
using Slice;
namespace RuntimeGeometryExample
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateBefore(typeof(RenderGraphBuilder))]
    public class GeometryTestMain : SystemBase
    {
        Entity CreateMeshEntity(float innerR, int innerN, float outerR, int outerN, int p, int q, float innerE, float outerE)
        {
            Entity e = EntityManager.CreateEntity();
            MeshBounds bounds;
            LitMeshRenderData lmrd;
            //MeshHelper.CreateSuperTorusKnotMesh(innerR, innerN, outerR, outerN, p, q, innerE, outerE, out bounds, out lmrd);
            //MeshHelper.CreateDonutMesh(innerR,innerN,outerR,outerN,out bounds, out lmrd);
            MeshHelper.CreateBoxMesh(outerR,out bounds, out lmrd);
           // MeshHelper.CreateSuperEllipsoidMesh(outerR, 0.5f, 1, 50, 50, out bounds, out lmrd);
            EntityManager.AddComponentData(e, lmrd);
            EntityManager.AddComponentData(e, bounds);
            return e;
        }

        Entity CreateCamera(float aspect, float4 background)
        {
            var ecam = EntityManager.CreateEntity(typeof(LocalToWorld), typeof(Translation), typeof(Rotation));
            var cam = new Camera();
            cam.clipZNear = 0.1f;
            cam.clipZFar = 50.0f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(background.x, background.y, background.z, background.w);
            cam.viewportRect = new Rect(0, 0, 1, 1);
            cam.aspect = aspect;
            cam.fov = 60;
            cam.mode = ProjectionMode.Perspective;
            EntityManager.AddComponentData(ecam, cam);
            EntityManager.SetComponentData(ecam, new Translation { Value = new float3(0, 0, -4.0f) });
            EntityManager.SetComponentData(ecam, new Rotation { Value = quaternion.identity });
            return ecam;
        }

        Entity CreateLitRenderer(Entity eMesh, Entity eMaterial, quaternion rot, float3 pos, float3 scale)
        {
            Entity erendLit = EntityManager.CreateEntity();

            var lmrd = EntityManager.GetComponentData<LitMeshRenderData>(eMesh);
            int indexCount = lmrd.Mesh.Value.Indices.Length;

            EntityManager.AddComponentData(erendLit, new MeshRenderer   // renderer -> maps to shader to use
            {
                material = eMaterial,
                mesh = eMesh,
                startIndex = 0,
                indexCount = indexCount
            });
            EntityManager.AddComponentData(erendLit, new LitMeshRenderer());
            EntityManager.AddComponentData(erendLit, new LocalToWorld
            {
                Value = float4x4.identity
            });
            EntityManager.AddComponentData(erendLit, new Translation
            {
                Value = pos
            });
            EntityManager.AddComponentData(erendLit, new Rotation
            {
                Value = rot
            });
            if (scale.x != scale.y || scale.y != scale.z)
            {
                EntityManager.AddComponentData(erendLit, new NonUniformScale
                {
                    Value = scale
                });
            }
            else if (scale.x != 1.0f)
            {
                EntityManager.AddComponentData(erendLit, new Scale
                {
                    Value = scale.x
                });
            }
            EntityManager.AddComponentData(erendLit, new WorldBounds());
            return erendLit;
        }

        bool created;

        float3 drawColor = new float3(1);
        int drawColorIndex = 0;
        static readonly float3[] colorPalette = new float3[]
        {
            new float3(1, 1, 1),
            new float3(1, .2f, .2f),
            new float3(.2f, 1, .2f),
            new float3(.2f, .2f, 1),
            new float3(1, 1, .2f),
            new float3(.2f, 1, 1),
            new float3(1, .2f, 1)
        };

        float drawSize = .05f;
        int drawSizeIndex = 1;
        static readonly float[] sizePalette = new float[]
        {
            .025f,
            .05f,
            .1f,
            .2f,
            .4f
        };

        int materialIndex = 0;

        Entity eCam;
        Entity ePlainMaterial;
        Entity eMetalMaterial;
        Entity eFirstKnot;
        Entity eMeshDonut;

        Entity eCurrentShape;
        Entity eCurrentMaterial;
        NativeList<float3> drawList;
        NativeList<Entity> strokeStack;
        NativeList<Entity> cameraList;

        Unity.Mathematics.Random random;

        ScreenToWorld s2w;

        public void CreateScene()
        {
            // one startup main camera
            eCam = CreateCamera(1920.0f / 1080.0f, new float4(1.2f, 1.2f, 1.2f, 1));

            // create meshes
            eMeshDonut = CreateMeshEntity(.1f, 20, .50f, 128, 3, 2, .9f, .8f);

            // create materials
            eMetalMaterial = EntityManager.CreateEntity();
            EntityManager.AddComponentData(eMetalMaterial, new LitMaterial
            {
                texAlbedoOpacity = Entity.Null,
                texMetal = Entity.Null,
                texNormal = Entity.Null,
                texEmissive = Entity.Null,
                constEmissive = new float3(0),
                constOpacity = 1.0f,
                constAlbedo = new float3(1),
                constMetal = 1.0f,
                constSmoothness = .68f,
                normalMapZScale = 1.0f,
                twoSided = false,
                transparent = false,
                scale = new float2(1, 1),
                offset = new float2(0, 0)
            });

            ePlainMaterial = EntityManager.CreateEntity();
            EntityManager.AddComponentData(ePlainMaterial, new LitMaterial
            {
                texAlbedoOpacity = Entity.Null,
                texMetal = Entity.Null,
                texNormal = Entity.Null,
                texEmissive = Entity.Null,
                constEmissive = new float3(0),
                constOpacity = 1.0f,
                constAlbedo = new float3(1),
                constMetal = 0.0f,
                constSmoothness = .18f,
                normalMapZScale = 1.0f,
                twoSided = false,
                transparent = false,
                scale = new float2(1, 1),
                offset = new float2(0, 0)
            });

            eCurrentMaterial = ePlainMaterial;

            // lights
            float3[] lightcolor = new float3[] { new float3(1.0f, .3f, .2f), new float3(.1f, 1.0f, .2f), new float3(.1f, .2f, 1.0f) };
            float3[] lightdir = new float3[] { new float3(-1, -1, 1), new float3(1, 1, 1), new float3(0, 1, 1) };
            Assert.IsTrue(lightdir.Length == lightcolor.Length);
            for (int i = 0; i < lightcolor.Length; i++)
            {
                Entity eDirLight = EntityManager.CreateEntity();
                EntityManager.AddComponentData(eDirLight, new Light
                {
                    intensity = .5f,
                    color = lightcolor[i]
                });
                EntityManager.AddComponentData(eDirLight, new DirectionalLight {});
                EntityManager.AddComponentData(eDirLight, new LocalToWorld {});
                EntityManager.AddComponentData(eDirLight, new NonUniformScale { Value = new float3(1) });
                EntityManager.AddComponentData(eDirLight, new Rotation
                {
                    Value = quaternion.LookRotationSafe(lightdir[i], new float3(1, 0, 0))
                });
            }

            // renderer
            eFirstKnot = CreateLitRenderer(eMeshDonut, eCurrentMaterial, quaternion.identity, new float3(0,0,0), new float3(1));
            EntityManager.AddComponentData(eFirstKnot, new DemoSpinner { spin = math.normalize(new quaternion(0, .2f, .1f, 1)) });
            strokeStack.Add(eFirstKnot);
        }

        protected override void OnCreate()
        {
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace; 
            drawList = new NativeList<float3>(Allocator.Persistent);
            strokeStack = new NativeList<Entity>(Allocator.Persistent);
            cameraList = new NativeList<Entity>(Allocator.Persistent);
            random = new Unity.Mathematics.Random(23);
        }

        protected override void OnDestroy()
        {
            cameraList.Dispose();
            drawList.Dispose();
            strokeStack.Dispose();
        }

        void BeginSlice(float2 inputPos)
        {
            var plane = GetSingleton<Plane>();
            plane.exist = true; 
            
            float3 pos = s2w.InputPosToWorldSpacePos(inputPos, 4.0f);
           // plane.pos = pos;
            plane.dist = pos.y;
            SetSingleton(plane); 
        }

        void ContinueStroke(float2 inputPos)
        {
 
        }

        void EndStroke()
        { 
            
        }

        protected override void OnUpdate()
        {
            if (!created)
            {
                CreateScene();
                s2w = World.GetExistingSystem<ScreenToWorld>();
                created = true;
                Entity cutplane  = EntityManager.CreateEntity();
                EntityManager.AddComponentData(cutplane, new Plane {normal = math.float3(0.0f, 1.0f, 0.0f), dist = 0, exist = false, pos = math.float3(0,0,0) });
            }

            // remove ambient lights. they get converted from multiple scenes right now
            Entities.WithAll<AmbientLight>().ForEach((Entity e, ref Light l) => {
                l.intensity = 0;
            }).Run();

            var di = GetSingleton<DisplayInfo>();
            float dt = World.Time.DeltaTime;
            var input = World.GetExistingSystem<InputSystem>();

            if (input.GetKey(KeyCode.LeftShift) || input.GetKey(KeyCode.RightShift))
                return;

            // new camera from current camera
            if (input.GetKeyDown(KeyCode.W))
            {
                if (cameraList.Length < 32)
                {
                    Entity eNewCam = EntityManager.Instantiate(eCam);
                    var cam = EntityManager.GetComponentData<Camera>(eNewCam);
                    cam.viewportRect.x = (cameraList.Length % 8) / 9.0f;
                    cam.viewportRect.width = 1.0f / 10.0f;
                    cam.viewportRect.y = (cameraList.Length / 8) / 9.0f;
                    cam.viewportRect.height = 1.0f / 10.0f;
                    cam.backgroundColor.r = random.NextFloat();
                    cam.backgroundColor.g = random.NextFloat();
                    cam.backgroundColor.b = random.NextFloat();
                    EntityManager.SetComponentData<Camera>(eNewCam, cam);
                    cameraList.Add(eNewCam);
                }
            } 
            // click to start drawing
            if (input.IsMousePresent())
            {
                if (input.GetMouseButtonDown(0))
                    BeginSlice(input.GetInputPosition());
                
            }
            else if (input.IsTouchSupported())
            {
                if (input.TouchCount() == 1)
                {
                    Touch t0 = input.GetTouch(0);
                    switch (t0.phase)
                    {
                        case TouchState.Began:
                            BeginSlice(new float2(t0.x, t0.y));
                            break;
                      
                    }
                }
            }
        }
    }
}
