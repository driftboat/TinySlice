using System; 
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Tiny.Rendering;
using UnityEngine;

namespace Slice
{
    //slice triangle to two part(up and down)
    [UpdateBefore(typeof(ComposeSystem))]
    public class SliceSystem : SystemBase
    { 
        
        protected override void OnUpdate()
        {
            var plane = GetSingleton<Plane>();
            if (!plane.exist) return;
            Entities.ForEach((ref Entity entity, in TrianglesSource triangleSource,
                in DynamicBuffer<DynamicTriangle> dt) =>
            {
                var triangles = dt.Reinterpret<LitTriangle>(); 
                NativeList<LitTriangle> upT = new NativeList<LitTriangle>(100, Allocator.TempJob);
                NativeList<LitTriangle> downT = new NativeList<LitTriangle>(100, Allocator.TempJob);
                NativeList<float3> crossP = new NativeList<float3>(100,Allocator.TempJob);
                for (int i = 0; i < triangles.Length; i++)
                {
                    var triangle = triangles[i];
                     SliceTriangle(ref triangle, ref plane, ref upT, ref downT, ref crossP);
                }
                
           

                if (upT.Length > 0 && downT.Length > 0)
                {
                    
                    NativeList<LitTriangle> upCrossT = new NativeList<LitTriangle>(10, Allocator.TempJob);
                    NativeList<LitTriangle> downCrossT = new NativeList<LitTriangle>(10, Allocator.TempJob);
                    if (crossP.Length > 2)
                    {
//                        Console.WriteLine("=============crossP:"+crossP.Length);
                        NativeList<float2> crossP2D = new NativeList<float2>(crossP.Length,Allocator.Temp);
                        ToCrossPanel2D(plane.normal, ref crossP, ref crossP2D);
                        NativeList<int> convexHullIndexes = new NativeList<int>(10, Allocator.TempJob);

                        GetConvexHull(ref crossP2D, ref convexHullIndexes);
                        crossP2D.Dispose();
                        LitVertex firtVertex = new LitVertex();
                        firtVertex.Position = crossP[convexHullIndexes[0]];
                        firtVertex.Albedo_Opacity = 1;
                    
                        for (int i = 1; i < convexHullIndexes.Length - 1; i++)
                        {
                            LitTriangle lt = new LitTriangle();
                            firtVertex .Normal = -plane.normal;
                            lt.vertexA = firtVertex;
                            LitVertex vertexB = new LitVertex();
                            vertexB.Position = crossP[convexHullIndexes[i]];
                            LitVertex vertexC = new LitVertex();
                            vertexC.Position = crossP[convexHullIndexes[i+1]];
                            vertexB.Albedo_Opacity = 1;
                            vertexC.Albedo_Opacity = 1;
                            vertexB.Normal = -plane.normal;
                            vertexC.Normal = -plane.normal;
                            lt.vertexB = vertexB;
                            lt.vertexC = vertexC; 
                         
                            upCrossT.Add(lt);
                            LitTriangle ltDown = new LitTriangle();
                            firtVertex .Normal = plane.normal;
                            vertexB.Normal = plane.normal;
                            vertexC.Normal = plane.normal;
                            ltDown.vertexA = firtVertex;
                            ltDown.vertexB = vertexC;
                            ltDown.vertexC = vertexB;
                            downCrossT.Add(ltDown);
                        }

                        convexHullIndexes.Dispose();
                    }

                    
                    
//                    Console.WriteLine("=============upT:"+upT.Length);
//                    Console.WriteLine("=============downT:"+downT.Length);
//                    Console.WriteLine("=============crossT:"+upCrossT.Length);
                    
                    var upEntity = EntityManager.CreateEntity();
                    EntityManager.AddBuffer<DynamicTriangle>(upEntity);
                    var dtUp = EntityManager.GetBuffer<DynamicTriangle>(upEntity);
                    dtUp.Reinterpret<LitTriangle>().AddRange(upT);
                    dtUp.Reinterpret<LitTriangle>().AddRange(upCrossT);

                    var downEntity = EntityManager.CreateEntity();
                    EntityManager.AddBuffer<DynamicTriangle>(downEntity);
                    var dtDown = EntityManager.GetBuffer<DynamicTriangle>(downEntity);
                    dtDown.Reinterpret<LitTriangle>().AddRange(downT);
                    dtDown.Reinterpret<LitTriangle>().AddRange(downCrossT);
                    EntityManager.DestroyEntity(entity);
                    EntityManager.DestroyEntity(triangleSource.sourceEntity);
                    upCrossT.Dispose();
                    downCrossT.Dispose();
                }
                else
                {
                    EntityManager.DestroyEntity(entity);
                }


                upT.Dispose();
                downT.Dispose();
                crossP.Dispose();
            }).WithoutBurst().WithStructuralChanges().Run();
        }

        static void ToCrossPanel2D(float3 normal,ref NativeList<float3> crossP, ref NativeList<float2> crossP2D )
        {
            float3 u = math.cross(normal,  math.up());
            if (u.Equals(float3.zero) )
            {
                u = math.cross(normal,  math.forward());
            }

            u = math.normalize(u);
            float3 v = math.cross(normal, u);
            for(int i=0; i< crossP.Length; i++)
            {
                var p = crossP[i];
                float px = (float)Math.Round( math.dot(p, u),2);
                float py = (float)Math.Round(math.dot(p, v),2);
                 
                float2 ap = new float2(px, py);
                if(!ExistPoint(ap, ref crossP2D))crossP2D.Add(ap);
                else
                {
                    crossP.RemoveAt(i);
                    i--;
                }
            }
        }

        static bool ExistPoint(float2 point, ref NativeList<float2> points)
        {
            foreach (var p in points)
            {
                if (math.abs(p.x - point.x) < 0.001f && math.abs(p.y - point.y) < 0.001f )
                {
                    return true;
                }
            }

            return false;
        }

        static string indexToLabel(int index)
        {
            char c = 'A';
            int charNo = index % 24;
            if (charNo == 23) charNo = 25;
            int b = index / 24;
            char cc = (char)(c + charNo);
            if (b == 0)
            {
                return "" + cc;
            }

            return ""+ cc + b;
        }

        static void GetConvexHull(ref NativeList<float2> crossP2D,ref NativeList<int> hullIndexes)
        {
            // https://en.wikipedia.org/wiki/Gift_wrapping_algorithm
            int minIndex = 0;
            float minx = float.MaxValue;
            int i = 0;
            for (; i < crossP2D.Length; i++)
            {
                if (crossP2D[i].x < minx)
                {
                    minIndex = i;
                    minx = crossP2D[i].x;
                }else if (crossP2D[i].x == minx && crossP2D[i].y < crossP2D[minIndex].y )
                {
                    minIndex = i; 
                }

            }
            hullIndexes.Add(minIndex);
            float2 pointOnHull = crossP2D[minIndex];
            i = 0;
            int pointOnHullIndex = minIndex;
            string testStr = "";
            while (true)
            {
                int selectEndPointIndex = 0;
                float2 endpoint = crossP2D[0];
                testStr += ("===start test point on hull :" + indexToLabel(pointOnHullIndex) + "\n");
                for (int j = 1; j < crossP2D.Length; j++)
                { 
                 
                    float2 testPoint = crossP2D[j];
                    if (selectEndPointIndex == pointOnHullIndex)
                    {
                        endpoint = testPoint;
                        selectEndPointIndex = j; 
                        testStr += ("  Contains:" + indexToLabel(j)  +","+ indexToLabel(selectEndPointIndex)  + "\n") ;
                    }
                    else
                    {
                        float side = SideOfPoint(pointOnHull, endpoint, testPoint);
                        if (side > math.EPSILON ||
                            math.abs(side) <= math.EPSILON && math.distance(testPoint, pointOnHull) >
                            math.distance(endpoint, pointOnHull))
                        {
                            float cal = ((endpoint.x - pointOnHull.x) * (testPoint.y - pointOnHull.y) -
                                         (endpoint.y - pointOnHull.y) * (testPoint.x - pointOnHull.x));
                            testStr += ("a:" + pointOnHull.x + "," + pointOnHull.y + "b:" + +endpoint.x + "," +
                                        endpoint.y + "c:" + +testPoint.x + "," + testPoint.y + "\n");
                            testStr += ("  isLeft:" + indexToLabel(j)  +","+ indexToLabel(selectEndPointIndex)  +"," +cal) + "\n"; 
                            
                            endpoint = testPoint;
                            selectEndPointIndex = j; 

                        }
                    }

                }

                if (selectEndPointIndex == minIndex)
                {
                    break;
                }
                else
                {
                    pointOnHullIndex = selectEndPointIndex;
                    hullIndexes.Add(selectEndPointIndex);
                    pointOnHull = endpoint;
                }

                i++;
                if (i > 40)
                { 
                    Console.Write(testStr);
                    for (int k = 0; k < crossP2D.Length; k++)
                    {
                        Console.WriteLine(" " + crossP2D[k].x + "," + crossP2D[k].y  );
                    }
                    for (int k = 0; k < hullIndexes.Length; k++)
                    {
                        Console.WriteLine("============================================overflow:"+k +"=" + indexToLabel(hullIndexes[k]) +":"+crossP2D[hullIndexes[k]].x + "," + crossP2D[hullIndexes[k]].y);
                    } 
                    break;
                }
            } 
           
        }
        
        static float SideOfPoint(float2 a, float2 b, float2 c){
            return ((b.x - a.x)*(c.y- a.y) - (b.y - a.y)*(c.x - a.x)) ;
        }
        
        static void SliceTriangle(ref LitTriangle tri, ref Plane plane, ref NativeList<LitTriangle> dtUp,
            ref NativeList<LitTriangle> dtDown, ref NativeList<float3> crossP)
        {
            LitVertex vertexA = tri.vertexA;
            LitVertex vertexB = tri.vertexB;
            LitVertex vertexC = tri.vertexC;
            float3 a = vertexA.Position;
            float3 b = vertexB.Position;
            float3 c = vertexC.Position;

            int sa = SideOf(a, plane);
            int sb = SideOf(b, plane);
            int sc = SideOf(c, plane);
            if ((sa == 1 || sb == 1 || sc == 1) && (sa == 2 || sb == 2 || sc == 2))
            {
                if (sa == 0)
                { 
                    //slice bc
                    float3 q;
                    float l;
                    Intersect(plane, b, c,out l, out q);
                    LitVertex vq = new LitVertex();
                    vq.Position = q;
                    vq.Normal = vertexB.Normal*l + vertexC.Normal*(1-l);
                    var t1 = new LitTriangle {vertexA = vertexA, vertexB = vertexB, vertexC = vq};
                    var t2 = new LitTriangle {vertexA = vertexA, vertexB = vq, vertexC = vertexC};
                    if (sb == 2)
                    {
                        dtDown.Add(t1);
                        dtUp.Add(t2);
                    }
                    else
                    {
                        dtUp.Add(t1);
                        dtDown.Add(t2);
                    }
                    
                    crossP.Add(a);
                    crossP.Add(q);
                }
                else if (sb == 0)
                { 
                    //slice ac
                    float3 q;
                    float l;
                    Intersect(plane, a, c, out l, out q);
                    LitVertex vq = new LitVertex();
                    vq.Position = q; 
                    vq.Normal = vertexA.Normal*l + vertexC.Normal*(1-l);
                    var t1 = new LitTriangle {vertexA = vertexA, vertexB = vertexB, vertexC = vq};
                    var t2 = new LitTriangle {vertexA = vertexB, vertexB = vertexC, vertexC = vq};
                    if (sa == 2)
                    {
                        dtDown.Add(t1);
                        dtUp.Add(t2);
                    }
                    else
                    {
                        dtUp.Add(t1);
                        dtDown.Add(t2);
                    }
                    crossP.Add(b);
                    crossP.Add(q);
                }
                else if (sc == 0)
                { 
                    //slice ab
                    float3 q;
                    float l;
                    Intersect(plane, a, b, out l,out q);
                    LitVertex vq = new LitVertex();
                    vq.Position = q; 
                    vq.Normal = vertexA.Normal*l + vertexB.Normal*(1-l);
                    var t1 = new LitTriangle {vertexA = vertexA, vertexB = vq, vertexC = vertexC};
                    var t2 = new LitTriangle {vertexA = vq, vertexB = vertexB, vertexC = vertexC};
                    if (sa == 2)
                    {
                        dtDown.Add(t1);
                        dtUp.Add(t2);
                    }
                    else
                    {
                        dtUp.Add(t1);
                        dtDown.Add(t2);
                    }
                    crossP.Add(c);
                    crossP.Add(q);
                }
                else if (sa != sb)
                {
                    float3 q1;
                    float3 q2;
                    float l1;
                    float l2;
                    //slice ab
                    Intersect(plane, a, b, out  l1,out q1);
                    LitVertex vq1 =   new LitVertex();
                    vq1.Position = q1;
                    vq1.Albedo_Opacity = 1;
                    vq1.Normal = vertexA.Normal*l1 + vertexB.Normal*(1-l1);
                    if (sa == sc)
                    {
                        //slice bc 
                        Intersect(plane, b, c,  out l2, out q2);
                        LitVertex vq2 = new LitVertex();
                        vq2.Position = q2;
                        vq2.Normal = vertexB.Normal*l2 + vertexC.Normal*(1-l2);
                        vq2.Albedo_Opacity = 1;
                        var t1 = new LitTriangle {vertexA = vertexA, vertexB = vq1, vertexC = vq2};
                        var t2 = new LitTriangle {vertexA = vertexA, vertexB = vq2, vertexC = vertexC};
                        var t3 = new LitTriangle {vertexA = vq1, vertexB = vertexB, vertexC = vq2};
                        if (sa == 2)
                        {
                            dtDown.Add(t1);
                            dtDown.Add(t2);
                            dtUp.Add(t3);
                        }
                        else
                        {
                            dtUp.Add(t1);
                            dtUp.Add(t2);
                            dtDown.Add(t3);
                        }
                        crossP.Add(q1);
                        crossP.Add(q2);
                    }
                    else
                    {
                        //slice ac  
                        Intersect(plane, c, a,  out l2, out q2);
                        LitVertex vq2 = new LitVertex();
                        vq2.Position = q2;
                        vq2.Normal =  vertexC.Normal*l2 + vertexA.Normal*(1-l2);
                        vq2.Albedo_Opacity = 1;
                        var t1 = new LitTriangle {vertexA = vertexA, vertexB = vq1, vertexC = vq2};
                        var t2 = new LitTriangle {vertexA = vq1, vertexB = vertexB, vertexC = vertexC};
                        var t3 = new LitTriangle {vertexA = vq1, vertexB = vertexC, vertexC = vq2};
                        if (sa == 2)
                        {
                            dtDown.Add(t1);
                            dtUp.Add(t2);
                            dtUp.Add(t3);
                        }
                        else
                        {
                            dtUp.Add(t1);
                            dtDown.Add(t2);
                            dtDown.Add(t3);
                        }
                        crossP.Add(q1);
                        crossP.Add(q2);
                    } 
                }
                else
                {
                    //slice ca 
                    float3 q1;
                    float3 q2;
                    float l1;
                    float l2;
                    //slice ab
                    Intersect(plane, c, a, out l1,out q1);
                    LitVertex vq1 = new LitVertex();
                    vq1.Position = q1; 
                    vq1.Normal = vertexC.Normal*l1 + vertexA.Normal*(1-l1);
                    vq1.Albedo_Opacity = 1;
                    //slice cb
                    Intersect(plane, b, c, out l2,out q2);
                    LitVertex vq2 = new LitVertex();
                    vq2.Position = q2;
                    vq2.Normal =  vertexB.Normal*l2 + vertexC.Normal*(1-l2);
                    vq2.Albedo_Opacity = 1;
                    var t1 = new LitTriangle {vertexA = vertexA, vertexB = vertexB, vertexC = vq1};
                    var t2 = new LitTriangle {vertexA = vq1, vertexB = vertexB, vertexC = vq2};
                    var t3= new LitTriangle {vertexA = vq1, vertexB = vq2, vertexC = vertexC};
                    if (sa == 2)
                    {
                        dtDown.Add(t1);
                        dtDown.Add(t2);
                        dtUp.Add(t3);
                    }
                    else
                    {
                        dtUp.Add(t1);
                        dtUp.Add(t2);
                        dtDown.Add(t3);
                    }
                    crossP.Add(q1);
                    crossP.Add(q2);
                }
            }
            else
            {
                int side = 1;
                if (sa != 0)
                {
                    side = sa;
                }

                if (sb != 0)
                {
                    side = sb;
                }

                if (sc != 0)
                {
                    side = sc;
                }

                if (side == 2)
                {
                    //down
                    dtDown.Add( new LitTriangle {vertexA = vertexA, vertexB = vertexB, vertexC = vertexC});
                }
                else
                {
                    //up
                    dtUp.Add( new LitTriangle {vertexA = vertexA, vertexB = vertexB, vertexC = vertexC});
                }
            }
 

        }

        static int SideOf(float3 pt, Plane plane)
        {
            float result = math.dot(plane.normal, pt) - plane.dist;
            if (result > math.EPSILON)
            {
                return 1;
            }

            if (result < -math.EPSILON)
            {
                return 2;
            }

            return 0;
        }
        
        static bool Intersect(Plane pl, float3 a, float3 b, out float l,out float3 q) {
            float3 normal = pl.normal;
            float3 ab = b - a;

            l = (pl.dist - math.dot(normal, a)) / math.dot(normal, ab);
 
            if (l >= -math.EPSILON && l <= (1 + math.EPSILON)) {
                q = a + l * ab;

                return true;
            }

            q = 0;

            return false;
        }
    }
}