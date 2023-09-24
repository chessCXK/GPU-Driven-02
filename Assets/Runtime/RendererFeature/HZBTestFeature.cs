using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class HZBTestFeature : ScriptableRendererFeature
{
    public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingShadows - 1;

    private HZBTestPass m_pass;
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_pass);
    }

    public override void Create()
    {
        m_pass = new HZBTestPass(renderPassEvent);
    }
}
public class HZBTestPass : ScriptableRenderPass
{
    private ComputeShader m_testHZBCs;

    private int m_hzbTestCs;
    private int m_clearArgsCs;

    private int m_standFrustumVertices = Shader.PropertyToID("_StandFrustumVertices");
    private int m__cullSpheres = Shader.PropertyToID("_CullSpheres");
    private int m_oneCSMObjCount = Shader.PropertyToID("_OneCSMObjCount");
    private int m_shadowArgsCount = Shader.PropertyToID("_ShadowArgsCount");

    private int m_csMaxSize= Shader.PropertyToID("_MaxSize");
    private int m_frustumPlanes = Shader.PropertyToID("_FrustumPlanes");
    private int m_cameraPos = Shader.PropertyToID("_CameraPos");
    private int m_cameraData = Shader.PropertyToID("_CameraData");
    private int m_lastVp = Shader.PropertyToID("_LastVp");
    private int m_hzbData = Shader.PropertyToID("_HzbData");

    private ProfilingSampler m_profilingSampler = new ProfilingSampler("HZBTestPass");

    private Plane[] m_cameraPlanes = new Plane[6];//原生获得的裁剪面

    private bool isCompile = false;

    private bool enableShadow = true;
    public HZBTestPass(RenderPassEvent renderPassEvent)
    {
        this.renderPassEvent = renderPassEvent;
        isCompile = true;
    }


    private void RefreshShadow()
    {
        if(enableShadow == false)
        {
            m_testHZBCs.DisableKeyword("_CSM0");
            m_testHZBCs.DisableKeyword("_CSM1");
            m_testHZBCs.DisableKeyword("_CSM2");
            return;
        }

        HiZGlobelManager m_gManager = HiZGlobelManager.Instance;
        m_gManager.CascadeDistances = HiZUtility.GetCascadeDistances();
        switch (m_gManager.CascadeDistances.Length)
        {
            case 0:
                m_testHZBCs.DisableKeyword("_CSM0");
                m_testHZBCs.DisableKeyword("_CSM1");
                m_testHZBCs.DisableKeyword("_CSM2");
                break;
            case 1:
                m_testHZBCs.EnableKeyword("_CSM0");
                m_testHZBCs.DisableKeyword("_CSM1");
                m_testHZBCs.DisableKeyword("_CSM2");
                break;
            case 2:
                m_testHZBCs.EnableKeyword("_CSM1");
                m_testHZBCs.EnableKeyword("_CSM0");
                m_testHZBCs.DisableKeyword("_CSM2");
                break;
            case 3:
                m_testHZBCs.EnableKeyword("_CSM2");
                m_testHZBCs.EnableKeyword("_CSM0");
                m_testHZBCs.EnableKeyword("_CSM1");
                break;
            default:
                m_testHZBCs.EnableKeyword("_CSM2");
                m_testHZBCs.EnableKeyword("_CSM0");
                m_testHZBCs.EnableKeyword("_CSM1");
                break;
        }
    }
    private void CheckEnvironment(ref RenderingData renderingData)
    {
        if (m_testHZBCs == null)
        {
            m_testHZBCs = Resources.Load<ComputeShader>("Shader/HZBTestCs");
            m_hzbTestCs = m_testHZBCs.FindKernel("HZBTest");
            m_clearArgsCs = m_testHZBCs.FindKernel("ClearArgs");
        }
        

        if(renderingData.shadowData.supportsMainLightShadows)
        {
            //阴影
            HiZGlobelManager m_gManager = HiZGlobelManager.Instance;
            float[] CascadeDistances = m_gManager.CascadeDistances;
            if (!enableShadow || isCompile || CascadeDistances == null || CascadeDistances.Length != renderingData.shadowData.mainLightShadowCascadesCount)
            {
                isCompile = false;
                enableShadow = true;
                RefreshShadow();
            }

        }
        else
        {
            if (enableShadow)
            {
                enableShadow = false;
                RefreshShadow();
            }
        }

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

        camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), distance, Camera.MonoOrStereoscopicEye.Mono, frustumCorners);

        for (int i = 0; i < 4; i++)
        {
            frustumCorners[i] = camera.transform.TransformPoint(frustumCorners[i]);
        }
        return frustumCorners;
    }


    public ComputeBuffer testBuffer;
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        HiZGlobelManager m_gManager = HiZGlobelManager.Instance;
        if (!m_gManager.IsSure)
        {
            return;
        }

        if (!renderingData.cameraData.camera.CompareTag("MainCamera"))
        {
            return;
        }
#if UNITY_EDITOR

        if (renderingData.cameraData.camera.name == "SceneCamera" ||
                renderingData.cameraData.camera.name == "Preview Camera")
            return;
#endif
        Camera camera = renderingData.cameraData.camera;
        CheckEnvironment(ref renderingData);

        CommandBuffer cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, m_profilingSampler))
        {
            float[] csmDistances = m_gManager.CascadeDistances;
            int csmCount = csmDistances.Length;
#if UNITY_EDITOR
            if (!m_gManager.enableDebugBuffer)
#endif
            {
                //阴影
                if (csmCount > 0 && m_gManager.ArgsShadowCount > 0)
                {
                    cmd.SetComputeIntParam(m_testHZBCs, m_shadowArgsCount, m_gManager.ArgsShadowCount);


                    cmd.SetComputeBufferParam(m_testHZBCs, m_clearArgsCs, HZBBufferName._ArgsShadowBuffer, m_gManager.ArgsShadowBuffer);
                }
            }
             

            //清除args
            cmd.SetComputeIntParam(m_testHZBCs, m_csMaxSize, m_gManager.argsCount);
            cmd.SetComputeBufferParam(m_testHZBCs, m_clearArgsCs, HZBBufferName._ArgsBuffer, m_gManager.ArgsBuffer);
            cmd.DispatchCompute(m_testHZBCs, m_clearArgsCs, Mathf.CeilToInt(m_gManager.argsCount / 64.0f), 1, 1);

            GeometryUtility.CalculateFrustumPlanes(camera, m_cameraPlanes);

            var frustumPlanes = new Vector4[6];
            for (int i = 0; i < 6; ++i)
            {
                Plane p = m_cameraPlanes[i];
                frustumPlanes[i] = new float4(p.normal, p.distance);
            }
#if UNITY_EDITOR
            
            if (m_gManager.enableDebugBuffer)
            {
                m_gManager.DispatchComputeDebug(cmd, m_hzbTestCs, m_testHZBCs);
            }
#endif
            bool enableShaodw = csmCount > 0 && m_gManager.ArgsShadowCount > 0;
#if UNITY_EDITOR
            if(m_gManager.enableDebugBuffer)
            {
                enableShaodw = false;
            }
#endif
                //阴影
            if (enableShaodw)
            {
                Vector3[] standFrustumV = GetFrustum8Point(camera);
                Vector4[] standFrustumVertices = new Vector4[8];
                for (int i = 0; i < 8; i++)
                {
                    standFrustumVertices[i] = new Vector4(standFrustumV[i].x, standFrustumV[i].y, standFrustumV[i].z, 0);
                }

                for (int i = 0; i < csmCount; i++)
                {
                    standFrustumVertices[i].w = csmDistances[i];
                }
                VisibleLight light = renderingData.lightData.visibleLights[renderingData.lightData.mainLightIndex];
                Vector3 mainLightDir = light.light.transform.forward;

                standFrustumVertices[3].w = camera.farClipPlane;
                standFrustumVertices[4].w = csmCount;
                standFrustumVertices[5].w = mainLightDir.x;
                standFrustumVertices[6].w = mainLightDir.y;
                standFrustumVertices[7].w = mainLightDir.z;
                cmd.SetComputeVectorArrayParam(m_testHZBCs, m_standFrustumVertices, standFrustumVertices);

                cmd.SetComputeVectorArrayParam(m_testHZBCs, m__cullSpheres, ShadowUtils.CullSpheres);

                cmd.SetComputeIntParam(m_testHZBCs, m_oneCSMObjCount, m_gManager.ShadowClusterCount);
                cmd.SetComputeIntParam(m_testHZBCs, m_shadowArgsCount, m_gManager.ArgsShadowCount);

                cmd.SetComputeBufferParam(m_testHZBCs, m_hzbTestCs, HZBBufferName._ResultShadowBuffer, m_gManager.ResultShadowBuffer);

                cmd.SetComputeBufferParam(m_testHZBCs, m_hzbTestCs, HZBBufferName._ArgsShadowBuffer, m_gManager.ArgsShadowBuffer);
            }

            //测试
            cmd.SetComputeVectorArrayParam(m_testHZBCs, m_frustumPlanes, frustumPlanes);
            cmd.SetComputeVectorParam(m_testHZBCs, m_cameraPos, camera.transform.position);
            cmd.SetComputeMatrixParam(m_testHZBCs, m_lastVp, m_gManager.LastVp);
            cmd.SetComputeVectorParam(m_testHZBCs, m_cameraData, new Vector4(camera.fieldOfView, QualitySettings.lodBias, QualitySettings.maximumLODLevel, 0));
            cmd.SetComputeVectorParam(m_testHZBCs, m_hzbData, new Vector4(m_gManager.HizTexutreRT.width, m_gManager.HizTexutreRT.height, m_gManager.NumMips - 1, 0));
            cmd.SetComputeIntParam(m_testHZBCs, m_csMaxSize, m_gManager.VData.clusterCount);
            cmd.SetComputeTextureParam(m_testHZBCs, m_hzbTestCs, HZBBufferName._HizTexutreRT, m_gManager.HizTexutreRT);
            cmd.SetComputeBufferParam(m_testHZBCs, m_hzbTestCs, HZBBufferName._ClusterBuffer, m_gManager.ClusterBuffer);
            cmd.SetComputeBufferParam(m_testHZBCs, m_hzbTestCs, HZBBufferName._ClusterKindBuffer, m_gManager.ClusterKindBuffer);
            cmd.SetComputeBufferParam(m_testHZBCs, m_hzbTestCs, HZBBufferName._ArgsBuffer, m_gManager.ArgsBuffer);
            cmd.SetComputeBufferParam(m_testHZBCs, m_hzbTestCs, HZBBufferName._ResultBuffer, m_gManager.ResultBuffer);
            cmd.DispatchCompute(m_testHZBCs, m_hzbTestCs, Mathf.CeilToInt(m_gManager.VData.clusterCount / 64.0f), 1, 1);

#if UNITY_EDITOR
            if (m_gManager.ArgsShadowBuffer != null && !m_gManager.enableDebugBuffer)
#else
            if (m_gManager.ArgsShadowBuffer != null)
#endif
            {
               /* uint[] data1 = new uint[m_gManager.ArgsShadowBuffer.count * 5];
                m_gManager.ArgsShadowBuffer.GetData(data1);

                HiZDataLoader f = camera.GetComponent<HiZDataLoader>();
                for (int c = 0; c < 5; c += 5)
                {
                    var ts = f.t;
                    if (ts != null && data1[c + 1] != 0)
                    {
                        ts.SetText(data1[c + 1].ToString());
                    }
                }

                if (data1[0 + 1] != 0)
                {
                    //Debug.Log("0级别视椎");
                }
                if (data1[5 + 1] != 0)
                {
                    //Debug.Log("1级别视椎");
                }
                if (data1[10 + 1] != 0)
                {
                    //Debug.Log("2级别视椎");
                }*/
            }
            
        }
        //执行
        context.ExecuteCommandBuffer(cmd);

        //回收
        CommandBufferPool.Release(cmd);
    }


}