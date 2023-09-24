using System.Collections.Generic;
using System.IO.Pipes;
using System.Net;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

public class CheckShadowInView : MonoBehaviour
{
    public GameObject objectToCheck;
    public Light mainLight;

    public GameObject sphere;

    public GameObject[] plane1;

    public float[] cascadeDistances;

    public int csmLevel = 0;

    void Start()
    {
        UniversalRenderPipelineAsset urpAsset = GraphicsSettings.renderPipelineAsset as UniversalRenderPipelineAsset;

        if (urpAsset != null)
        {
            cascadeDistances = GetShadowCascadesDistances(urpAsset);
            foreach (var ff in cascadeDistances)
                Debug.Log(ff);
        }
    }

    float[] GetShadowCascadesDistances(UniversalRenderPipelineAsset urpAsset)
    {
        int cascadeCount = urpAsset.shadowCascadeCount;
        float[] cascadeDistances = new float[cascadeCount];

        switch (cascadeCount)
        {
            case 2:
                cascadeDistances[0] = urpAsset.cascade2Split;
                break;
            case 3:
                Vector2 cascade3Splits = urpAsset.cascade3Split;
                cascadeDistances[0] = cascade3Splits.x;
                cascadeDistances[1] = cascade3Splits.y;
                break;
            case 4:
                Vector3 cascade4Splits = urpAsset.cascade4Split;
                cascadeDistances[0] = cascade4Splits.x;
                cascadeDistances[1] = cascade4Splits.y;
                cascadeDistances[2] = cascade4Splits.z;
                break;
        }

        float shadowDistance = urpAsset.shadowDistance;
        for (int i = 0; i < cascadeDistances.Length - 1; i++)
        {
            cascadeDistances[i] *= shadowDistance;
        }
        cascadeDistances[cascadeDistances.Length - 1] = shadowDistance;

        return cascadeDistances;
    }

    void Update()
    {
        Vector4 v4 = ShadowUtils.CullSpheres[1];
        sphere.transform.position = new Vector3(v4.x, v4.y, v4.z);
        sphere.transform.localScale = Vector3.one * v4.w * 2;

        Bounds bounds = objectToCheck.GetComponent<Renderer>().bounds;

        Vector3[] StandFrustumVertices = GetFrustum8Point(Camera.main);

        Vector3 minYVertex = StandFrustumVertices[0];
        for (int i = 1; i < StandFrustumVertices.Length; i++)
        {
            if (StandFrustumVertices[i].y < minYVertex.y)
            {
                minYVertex = StandFrustumVertices[i];
            }
        }
        float frustumMinY = minYVertex.y;

        Vector3[] frustumVertices = new Vector3[8];
        StandFrustumVertices.CopyTo(frustumVertices, 0);

        List<Vector3[]> shadowLine = Calculate8ShadowLine(bounds.center, bounds.extents, mainLight.transform.forward, frustumMinY);

        List<Vector3[]> frustum6Face = GetFrustum6Face(StandFrustumVertices);
        /*for (int j = 0; j < 4; j++)
        {
            plane1[j + 4].transform.position = frustum5Face[3][j];
        }*/

        float2[] IntersectResult = new float2[3];
        bool isIntersecting = CalculateBoundShadowIntersect(shadowLine, frustum6Face, ShadowUtils.CullSpheres, ref IntersectResult);
        if (!isIntersecting)
        {
            return;
        }
        return;
        float2 InAndOut0 = IntersectResult[0];
        float2 InAndOut1 = IntersectResult[1];
        float2 InAndOut2 = IntersectResult[2];
        if (InAndOut0.x > 0)
        {
            Debug.Log("落在0级别视椎");
        }

       if(InAndOut0.y * InAndOut1.x > 0)
        {
            Debug.Log("落在1级别视椎");
        }

        if (InAndOut1.y * InAndOut2.x > 0)
        {
            Debug.Log("落在2级别视椎");
        }
        
    }


    Vector3Int[] axisMuti =
        {
            new Vector3Int(1, 1, 1),
            new Vector3Int(1, 1, -1),
            new Vector3Int(1, -1, 1),
            new Vector3Int(1, -1, -1),
            new Vector3Int(-1, 1, 1),
            new Vector3Int(-1, 1, -1),
            new Vector3Int(-1, -1, 1),
            new Vector3Int(-1, -1, -1)
        };


    //计算8条线
    List<Vector3[]> Calculate8ShadowLine(Vector3 center, Vector3 extents, Vector3 lightDirection, float frustumMinY)
    {
        float minY = center.y - extents.y;
        if (frustumMinY > minY)
        {
            frustumMinY = minY;
        }
        List<Vector3[]> shadowLine = new List<Vector3[]>();
        
        //包围盒8个点的投影连接线
        for (uint i = 0; i < 8; i++)
        {
            Vector3[] line = new Vector3[2];
            line[0] = center + new Vector3(extents.x * axisMuti[i].x, extents.y * axisMuti[i].y, extents.z * axisMuti[i].z); // 最上面左前顶点
            line[1] = GetShadowDirectionLine(line[0], frustumMinY, lightDirection);
            shadowLine.Add(line);
        }
        return shadowLine;
    }
    //4条
    //IntersectResult:x表示香蕉了，y表示有没交点在球外
    bool CalculateBoundShadowIntersect(List<Vector3[]> shadowLine, List<Vector3[]> frustum6Face, Vector4[] cullSpheres, ref float2[] IntersectResult)
    {
        bool IntersectFrustum = false;

        float2 inAndOut0 = 0;
        float2 inAndOut1 = 0;
        float2 inAndOut2 = 0;
        //包围盒8个点的投影连接线
        for (int i = 0; i < 8; i++)
        {
            Vector3 startPoint = shadowLine[i][0]; // 最上面左前顶点
            Vector3 endPoint = shadowLine[i][1];
            Debug.DrawLine(startPoint, endPoint, Color.red, 0);

            foreach (var face in frustum6Face)
            {

                bool isIntersect = CheckLinePlaneIntersection(face, startPoint, endPoint, out var intersectionPoint);

                if (isIntersect == false)
                {
                    continue;
                }
                IntersectFrustum |= isIntersect;

                if (cullSpheres.Length > 0)
                {
                    inAndOut0 += CalculateIntersectStatus(startPoint, endPoint, intersectionPoint, cullSpheres[0]);
                }

                if (cullSpheres.Length > 1)
                {
                    inAndOut1 += CalculateIntersectStatus(startPoint, endPoint, intersectionPoint, cullSpheres[1]);
                }

                if (cullSpheres.Length > 2)
                {
                    inAndOut2 += CalculateIntersectStatus(startPoint, endPoint, intersectionPoint, cullSpheres[2]);
                }
            }
        }

        IntersectResult[0] = inAndOut0;
        IntersectResult[1] = inAndOut1;
        IntersectResult[2] = inAndOut2;
        return IntersectFrustum;
    }

    float2 CalculateIntersectStatus(Vector3 startPoint, Vector3 endPoint, Vector3 intersectionPoint, Vector4 cullSphere)
    {
        float2 InAndOut0 = 0;
        Vector3 sphereCenter = new Vector3(cullSphere.x, cullSphere.y, cullSphere.z);
        float sphereRadius = cullSphere.w;

        // 计算直线与球心的距离,小于半径就相交
        // 计算直线与圆心的距离,小于半径就相交
        Vector3 start_sphereCenter = startPoint - sphereCenter;
        Vector3 sToe = (startPoint - endPoint).normalized;

        float toCenterDis = Vector3.Dot(start_sphereCenter, sToe);
        float xiebian1 = start_sphereCenter.magnitude;
        float toLineDis = Mathf.Sqrt(xiebian1 * xiebian1 - toCenterDis * toCenterDis);
        int lineIntersect = sphereRadius > toLineDis ? 1 : 0;//直线相交

        float xiebian2 = (endPoint - sphereCenter).magnitude;
        if (xiebian1 <= sphereRadius || xiebian2 <= sphereRadius)
        {
            InAndOut0.x = 1;
        }

        // 计算圆心到直线的垂线与直线的交点
        Vector3 closestPoint = startPoint + sToe * -toCenterDis;
        Vector3 pa = startPoint - closestPoint;
        Vector3 pb = endPoint - closestPoint;

        InAndOut0.x += Vector3.Dot(pa, pb) < 0 ? 1 : 0;
        InAndOut0.x *= lineIntersect;


        //与视椎的交点是否在球外
        float distance = Vector3.Distance(sphereCenter, intersectionPoint);
        InAndOut0.y = sphereRadius > distance ? 0 : 1;
        return InAndOut0;
    }

    Vector3 GetShadowDirectionLine(Vector3 topPoint, float frustumMinY, Vector3 lightDirection)
    {
        Vector3 bottomPoint = new Vector3(topPoint.x, frustumMinY, topPoint.z); // 最下面的顶点

        //计算阴影长度
        Vector3 objNormal = (bottomPoint - topPoint);
        float angle = Vector3.Angle(lightDirection, objNormal);
        float shadowLen = Mathf.Abs(Mathf.Tan(angle * Mathf.Deg2Rad)) * (topPoint.y - bottomPoint.y);

        //水平方向偏移
        lightDirection.y = 0;
        Vector3 forward = (lightDirection * shadowLen + bottomPoint) - bottomPoint;

        Vector3 shadowEndPoint = forward.normalized * shadowLen + bottomPoint;

        return shadowEndPoint;
    }


    Vector3 CalculatePlaneNormal(Vector3[] vertices)
    {
        Vector3 edge1 = vertices[1] - vertices[0];
        Vector3 edge2 = vertices[2] - vertices[0];
        return Vector3.Cross(edge1, edge2).normalized;
    }

    float SignedDistanceToPoint(Vector3 point, Vector3 planePoint, Vector3 planeNormal)
    {
        return Vector3.Dot(point - planePoint, planeNormal);
    }

    bool CheckLinePlaneIntersection(Vector3[] planeVertices, Vector3 linePointA, Vector3 linePointB, out Vector3 intersectionPoint)
    {
        intersectionPoint = Vector3.zero;
        Vector3 planeNormal = CalculatePlaneNormal(planeVertices);
        float distanceA = SignedDistanceToPoint(linePointA, planeVertices[0], planeNormal);
        float distanceB = SignedDistanceToPoint(linePointB, planeVertices[0], planeNormal);

        if (distanceA * distanceB >= 0)
        {
            return false; // 两个点在平面的同一侧，不相交
        }

        // 计算交点
        float t = distanceA / (distanceA - distanceB);
        intersectionPoint = linePointA + t * (linePointB - linePointA);
        // 检查交点是否在四边形内
        return PointInPolygon(planeVertices, intersectionPoint);
    }

    bool PointInPolygon(Vector3[] vertices, Vector3 point)
    {
        var A = vertices[0];
        var B = vertices[1];
        var C = vertices[2];
        var D = vertices[3];
        Vector3 AB = A - B;
        Vector3 AP = A - point;
        Vector3 CD = C - D;
        Vector3 CP = C - point;

        Vector3 DA = D - A;
        Vector3 DP = D - point;
        Vector3 BC = B - C;
        Vector3 BP = B - point;
        
        double isBetweenAB_CD = (Mathf.Sign(Vector3.Dot(Vector3.Cross(AB, AP), Vector3.Cross(CD, CP))) + 1) * 0.5;
        double isBetweenDA_BC = (Mathf.Sign(Vector3.Dot(Vector3.Cross(DA, DP), Vector3.Cross(BC, BP))) + 1) * 0.5;
        uint abcd = (uint)isBetweenAB_CD;
        uint dabc = (uint)isBetweenDA_BC;

        return abcd * dabc > 0;
    }


    //视椎体面

    Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        return a + (b - a) * t;
    }

    List<Vector3[]> GetFrustum6Face(Vector3[] frustumVertices)
    {
        // 近平面
        Vector3[] nearPlaneVertices = new Vector3[] {
            frustumVertices[0], // 左下角的近裁剪面顶点
            frustumVertices[1], // 左上角的近裁剪面顶点
            frustumVertices[2], // 右上角的近裁剪面顶点
            frustumVertices[3]  // 右下角的近裁剪面顶点
        };

        // 远平面
        Vector3[] farPlaneVertices = new Vector3[] {
            frustumVertices[4], // 左下角的远裁剪面顶点
            frustumVertices[5], // 左上角的远裁剪面顶点
            frustumVertices[6], // 右上角的远裁剪面顶点
            frustumVertices[7]  // 右下角的远裁剪面顶点
        };

        // 左侧面
        Vector3[] leftPlaneVertices = new Vector3[] {
            frustumVertices[0], 
            frustumVertices[1], 
            frustumVertices[5], 
            frustumVertices[4]  
        };

        // 右侧面
        Vector3[] rightPlaneVertices = new Vector3[] {
            frustumVertices[3], 
            frustumVertices[2], 
            frustumVertices[6], 
            frustumVertices[7]  
        };

        // 上侧面
        Vector3[] topPlaneVertices = new Vector3[] {
            frustumVertices[1], 
            frustumVertices[2], 
            frustumVertices[6], 
            frustumVertices[5]  
        };

        // 下侧面
        Vector3[] bottomPlaneVertices = new Vector3[] {
            frustumVertices[0], 
            frustumVertices[3], 
            frustumVertices[7], 
            frustumVertices[4]  
        };

        List<Vector3[]> frustumFace = new List<Vector3[]>();

        frustumFace.Add(nearPlaneVertices);
        frustumFace.Add(farPlaneVertices);
        frustumFace.Add(leftPlaneVertices);
        frustumFace.Add(rightPlaneVertices);
        frustumFace.Add(topPlaneVertices);
        frustumFace.Add(bottomPlaneVertices);

        return frustumFace;
    }

    Vector3[] GetFrustum8Point(Camera camera)
    {
        Vector3[] nearCorners = GetFrustumCorners(camera, camera.nearClipPlane);
        Vector3[] farCorners = GetFrustumCorners(camera, camera.farClipPlane);

        // Combine the near and far corners to create the 8 vertices of the frustum.
        Vector3[] frustumVertices = new Vector3[8];
        nearCorners.CopyTo(frustumVertices, 0);
        farCorners.CopyTo(frustumVertices, 4);
        return frustumVertices;
    }

    private Vector3[] GetFrustumCorners(Camera camera, float distance)
    {
        Vector3[] frustumCorners = new Vector3[4];
        Vector3 eulerAngles = camera.transform.eulerAngles;
        bool jiaoZheng = Mathf.Abs(eulerAngles.y) % 90 == 0;
        if (jiaoZheng)
        {
            //矫正
            camera.transform.eulerAngles = eulerAngles + new Vector3(0, 0.001f, 0);
        }
        camera.transform.eulerAngles = eulerAngles;
        camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), distance, Camera.MonoOrStereoscopicEye.Mono, frustumCorners);
        for (int i = 0; i < 4; i++)
        {
            frustumCorners[i] = camera.transform.TransformPoint(frustumCorners[i]);
        }
        return frustumCorners;
    }
}
