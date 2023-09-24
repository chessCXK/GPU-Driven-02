#ifndef SHADOWCULL
#define SHADOWCULL

#pragma multi_compile_local __ _CSM0
#pragma multi_compile_local __ _CSM1
#pragma multi_compile_local __ _CSM2


//#pragma multi_compile _CSM0

#define deg2Rad 0.0174532924f

struct ShadowLine
{
    half3 vertices[2];
};

struct ShadowLine8
{
    ShadowLine shadowLine[8];
};

struct FrustumFace
{
    half3 vertices[4];
};

struct Frustum6Face
{
    FrustumFace frustumFace[6];
};

half3 CalculatePlaneNormal(FrustumFace face)
{
    half3 edge1 = face.vertices[1] - face.vertices[0];
    half3 edge2 = face.vertices[2] - face.vertices[0];
    return normalize(cross(edge1, edge2));
}

half SignedDistanceToPoint(half3 pos, half3 planePoint, half3 planeNormal)
{
    return dot(pos - planePoint, planeNormal);
}

//判断点是否在面内部
uint PointInPolygon(FrustumFace face, half3 pos)
{
    half3 A = face.vertices[0];
    half3 B = face.vertices[1];
    half3 C = face.vertices[2];
    half3 D = face.vertices[3];
    half3 AB = A - B;
    half3 AP = A - pos;
    half3 CD = C - D;
    half3 CP = C - pos;

    half3 DA = D - A;
    half3 DP = D - pos;
    half3 BC = B - C;
    half3 BP = B - pos;

    uint isBetweenAB_CD = (sign(dot(cross(AB, AP), cross(CD, CP))) + 1) * 0.5;
    uint isBetweenDA_BC = (sign(dot(cross(DA, DP), cross(BC, BP))) + 1) * 0.5;
    return isBetweenAB_CD & isBetweenDA_BC;
}

//检查线与面相交
uint CheckLinePlaneIntersection(FrustumFace face, half3 linePointA, half3 linePointB, out half3 intersectionPoint)
{
    half3 planeNormal = CalculatePlaneNormal(face);
    half distanceA = SignedDistanceToPoint(linePointA, face.vertices[0], planeNormal);
    half distanceB = SignedDistanceToPoint(linePointB, face.vertices[0], planeNormal);

    //if(abs(distanceA) < abs(distanceB))
    if (distanceA * distanceB >= 0)
    {
        return 0; // 两个点在平面的同一侧，不相交
    }

    // 计算交点
    intersectionPoint = linePointA + distanceA * (linePointB - linePointA) / (distanceA - distanceB);
    // 检查交点是否在四边形内
    return PointInPolygon(face, intersectionPoint);
}

//计算点在阴影投射方向的终点
half3 GetShadowDirectionLine(half3 topPoint, half frustumMinY, half3 lightDirection)
{
    half3 bottomPoint = half3(topPoint.x, frustumMinY, topPoint.z); // 最下面的顶点

    //计算阴影长度
    half ther = angle(lightDirection, half3(0, 1, 0));
    half shadowLen = abs(tan(ther * deg2Rad)) * (topPoint.y - bottomPoint.y);

    //水平方向偏移
    lightDirection.y = 0;
    half3 forward = (lightDirection * shadowLen + bottomPoint) - bottomPoint;

    half3 shadowEndPoint = normalize(forward) * shadowLen + bottomPoint;
    return shadowEndPoint;
}

//根据包围盒计算8条投影线
ShadowLine8 Calculate8ShadowLine(half3 center, half3 extents, half3 lightDirection, half frustumMinY)
{
    half minY = center.y - extents.y;
    frustumMinY = min(frustumMinY, minY);

    ShadowLine8 shadowLine8;

    //包围盒8个点的投影连接线
    [unroll]
    for (uint i = 0; i < 8; i++)
    {
        shadowLine8.shadowLine[i].vertices[0] = center + half3(extents.x * axisMuti[i].x, extents.y * axisMuti[i].y, extents.z * axisMuti[i].z); // 最上面左前顶点
        shadowLine8.shadowLine[i].vertices[1] = GetShadowDirectionLine(shadowLine8.shadowLine[i].vertices[0], frustumMinY, lightDirection);
    }
    return shadowLine8;
}

//计算根据当前lenght阴影距离计算距离length远的视椎平面
FrustumFace CalculateMidPlane(half4 frustumVertices[8], half farClipPlane, half lenght, half ShadowLenght)
{
    FrustumFace midPlane;

    half scale = ShadowLenght / farClipPlane;
    half t = lenght / ShadowLenght * scale;

    [unroll]
    for (uint i = 0; i < 4; i++)
    {
        midPlane.vertices[i] = lerp(frustumVertices[i].xyz, frustumVertices[i + 4].xyz, t);
    }

    return midPlane;
}

//根据远近平面8个点获得6个视椎体平面
Frustum6Face GetFrustum6Face(half3 frustumVertices[8])
{
    // near
     half3 nearPlaneVertices[4] = {
        frustumVertices[0],
        frustumVertices[1],
        frustumVertices[2],
        frustumVertices[3]
    };

    // far
    half3 farPlaneVertices[4] = {
        frustumVertices[4],
        frustumVertices[5],
        frustumVertices[6],
        frustumVertices[7]
    };

    // left
    half3 leftPlaneVertices[4] = {
        frustumVertices[0],
        frustumVertices[1],
        frustumVertices[5],
        frustumVertices[4]
    };

    // right
    half3 rightPlaneVertices[4] = {
        frustumVertices[3],
        frustumVertices[2],
        frustumVertices[6],
        frustumVertices[7]
    };

    // top
    half3 topPlaneVertices[4] = {
        frustumVertices[1],
        frustumVertices[2],
        frustumVertices[6],
        frustumVertices[5]
    };

    // down
    half3 bottomPlaneVertices[4] = {
        frustumVertices[0],
        frustumVertices[3],
        frustumVertices[7],
        frustumVertices[4]
    };
    Frustum6Face frustum6Face;
    frustum6Face.frustumFace[0] = nearPlaneVertices;
    frustum6Face.frustumFace[1] = farPlaneVertices;
    frustum6Face.frustumFace[2] = leftPlaneVertices;
    frustum6Face.frustumFace[3] = rightPlaneVertices;
    frustum6Face.frustumFace[4] = topPlaneVertices;
    frustum6Face.frustumFace[5] = bottomPlaneVertices;
    return frustum6Face;
}

 half2 CalculateIntersectStatus(half3 startPoint, half3 endPoint, half3 intersectionPoint, half4 cullSphere)
{
    half2 inAndOut0 = 0;
    half3 sphereCenter = half3(cullSphere.x, cullSphere.y, cullSphere.z);
    half sphereRadius = cullSphere.w;

    // 计算直线与球心的距离,小于半径就相交
    half3 start_sphereCenter = startPoint - sphereCenter;
    half3 sToe = normalize(startPoint - endPoint);
    half toCenterDis = dot(start_sphereCenter, sToe);
    half xiebian1 = distance(startPoint, sphereCenter);
    half toLineDis = sqrt(xiebian1 * xiebian1 - toCenterDis * toCenterDis);
    uint lineIntersect = sphereRadius > toLineDis ? 1 : 0;//直线相交

    bool xiebian1Inside = xiebian1 <= sphereRadius;
    bool xiebian2Inside = distance(endPoint, sphereCenter) <= sphereRadius;
    
    inAndOut0.x = (xiebian1Inside ? 1 : 0) | (xiebian2Inside ? 1 : 0);

    // 计算圆心到直线的垂线与直线的交点
    half3 closestPoint = startPoint + sToe * -toCenterDis;
    half3 pa = startPoint - closestPoint;
    half3 pb = endPoint - closestPoint;

    inAndOut0.x += dot(pa, pb) < 0 ? 1 : 0;
    inAndOut0.x *= lineIntersect;


    //与视椎的交点是否在球外
    half dis = distance(sphereCenter, intersectionPoint);
    inAndOut0.y = sphereRadius > dis ? 0 : 1;
    return inAndOut0;
}

//计算包围盒投影是否与视椎面相交并在面内
uint CalculateBoundShadowIntersect(ShadowLine8 shadowLine8, Frustum6Face frustum6Face, half4 cullSpheres[3], out half2 intersectResult[3])
{
    uint isIntersecting = 0;

    half2 inAndOut0 = 0;
    half2 inAndOut1 = 0;
    half2 inAndOut2 = 0;
    //包围盒8个点的投影连接线
    [unroll]
    for (uint i = 0; i < 8; i++)
    {
        half3 startPoint = shadowLine8.shadowLine[i].vertices[0]; // 最上面左前顶点
        half3 endPoint = shadowLine8.shadowLine[i].vertices[1];
        [unroll]
        for (uint j = 0; j < 6; j++)
        {
            half3 intersectionPoint;
            uint isIntersect = CheckLinePlaneIntersection(frustum6Face.frustumFace[j], startPoint, endPoint, intersectionPoint);

            if (isIntersect == 0)
            {
                continue;
            }
           isIntersecting |= isIntersect;


           inAndOut0 += CalculateIntersectStatus(startPoint, endPoint, intersectionPoint, cullSpheres[0]);
#if defined(_CSM1) || defined(_CSM2)
           inAndOut1 += CalculateIntersectStatus(startPoint, endPoint, intersectionPoint, cullSpheres[1]);
#endif

#if defined(_CSM2)
           inAndOut2 += CalculateIntersectStatus(startPoint, endPoint, intersectionPoint, cullSpheres[2]);
#endif
        }
    }

    intersectResult[0] = inAndOut0;
    intersectResult[1] = inAndOut1;
    intersectResult[2] = inAndOut2;
    return isIntersecting;
}


RWBuffer<uint> _ResultShadowBuffer;

RWBuffer<uint> _ArgsShadowBuffer;

//目前只给支持3级CSM
half4 _CullSpheres[3];

//视椎8个顶点，(xyz是顶点世界位置，8个z代表 0：csm0的距离，1：csm1的距离，2：csm2的距离，3：farClipPlane，4：CSM Count，5：lightDirX，6：lightDirY，7：lightDirZ)
half4 _StandFrustumVertices[8];

uint _OneCSMObjCount;

uint _ShadowArgsCount;

void ShadowCull(ClusterKindData cKindData, ClusterData cData)
{
    half3 frustumVertices[8];

    half frustumMinY = _StandFrustumVertices[0].y;
    [unroll]
    for (uint i = 0; i < 8; i++)
    {
        half3 vertex = _StandFrustumVertices[i].xyz;
        frustumMinY = min(frustumMinY, vertex.y);
        frustumVertices[i] = vertex;
    }

    half csm0Distance = _StandFrustumVertices[0].w;
    half csm1Distance = _StandFrustumVertices[1].w;
    half csm2Distance = _StandFrustumVertices[2].w;
    half farClipPlane = _StandFrustumVertices[3].w;
    uint csmCount = _StandFrustumVertices[4].w;
    half3 lightDirection = half3(_StandFrustumVertices[5].w, _StandFrustumVertices[6].w, _StandFrustumVertices[7].w);
    uint csmEndDistance = _StandFrustumVertices[csmCount - 1].w;

    ShadowLine8 shadowLine8 = Calculate8ShadowLine(cData.center, cData.extends, lightDirection, frustumMinY);
    Frustum6Face frustum6Face = GetFrustum6Face(frustumVertices);

    half2 intersectResult[3];
    bool isIntersecting = CalculateBoundShadowIntersect(shadowLine8, frustum6Face, _CullSpheres, intersectResult);
    if (!isIntersecting)
    {
        return;
    }

    half2 inAndOut0 = intersectResult[0];
    half2 inAndOut1 = intersectResult[1];
    half2 inAndOut2 = intersectResult[2];

    uint argsShadowIndex = cKindData.argsShadowIndex;

    if (inAndOut0.x > 0)
    {
        //相交与0级CSM
        uint currentIndex;
        InterlockedAdd(_ArgsShadowBuffer[argsShadowIndex * 5 + 1], 1, currentIndex);
        _ResultShadowBuffer[cKindData.kindShadowResultStart + currentIndex] = cData.clusterIndex;
    }

#if defined(_CSM1) || defined(_CSM2)
    if (inAndOut0.y * inAndOut1.x > 0)
    {
        //相交与1级CSM
        uint currentIndex;
        InterlockedAdd(_ArgsShadowBuffer[argsShadowIndex * 5 + 1 + _ShadowArgsCount * 5], 1, currentIndex);
        _ResultShadowBuffer[cKindData.kindShadowResultStart + currentIndex + _OneCSMObjCount] = cData.clusterIndex;
    }
#endif

#if defined(_CSM2)
    if (inAndOut1.y * inAndOut2.x > 0)
    {
        //相交与2级CSM
        uint currentIndex;
        InterlockedAdd(_ArgsShadowBuffer[argsShadowIndex * 5 + 1 + _ShadowArgsCount * 10], 1, currentIndex);
        _ResultShadowBuffer[cKindData.kindShadowResultStart + currentIndex + _OneCSMObjCount * 2] = cData.clusterIndex;
    }
#endif
}

void ClearShadowArgs(uint id)
{
    if (id >= _ShadowArgsCount)
    {
        return;
    }

    _ArgsShadowBuffer[id * 5 + 1] = 0;

#if defined(_CSM1) || defined(_CSM2)
    _ArgsShadowBuffer[id * 5 + 1 + _ShadowArgsCount * 5] = 0;
#endif

#if defined(_CSM2)
    _ArgsShadowBuffer[id * 5 + 1 + _ShadowArgsCount * 10] = 0;
#endif
}

#endif