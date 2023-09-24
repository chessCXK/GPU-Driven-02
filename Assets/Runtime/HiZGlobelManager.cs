using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;


public class HiZGlobelManager
{
    private bool m_isSure = false;

#if UNITY_EDITOR
    public bool DontRunHZBTest = false;

    public bool enableDebugBuffer = false;

    //激活完自动设置为false
    public bool ClearargsDebugBuffer = false;

    private ComputeShader m_testHZBCs;
    private ComputeBuffer m_visibleArgsBuffer;
    private int m_argsDebugCs = -1;
    private int m_csMaxSize = Shader.PropertyToID("_MaxSize");

    private uint[] m_ClearData;
#endif
    #region 多个Pass公用的数据
    private ComputeBuffer m_clusterBuffer;
    private ComputeBuffer m_clusterKindBuffer;

    private ComputeBuffer m_argsBuffer;

    private ComputeBuffer m_resultBuffer;

    //Begin阴影
    private ComputeBuffer m_argsShadowBuffer;
    private ComputeBuffer m_resultShadowBuffer;
    private float[] m_cascadeDistances;
    private int m_argsShadowCount;
    private int m_shadowClusterCount;
    //End阴影
    private VegetationData m_vData;

    public int argsCount;

    public int NumMips = -1;
    public RenderTexture HizTexutreRT;
    public Matrix4x4 LastVp;
    public Vector2Int HzbSize = Vector2Int.zero;

    public void CreateComputeBuffer(VegetationData vData)
    {
        if(vData == null || vData.clusterData == null)
        {
            return;
        }
        DisposeComputeBuffer();

        this.m_vData = vData;
        m_clusterBuffer?.Release();
        m_clusterBuffer = new ComputeBuffer(vData.clusterCount, Marshal.SizeOf(typeof(ClusterData)));
        m_clusterBuffer.SetData(vData.clusterData);

        m_resultBuffer?.Release();
        m_resultBuffer = GeneateResultBuffer(vData);

        CascadeDistances = HiZUtility.GetCascadeDistances();

        RunTimeCreateBuffer(vData);
        
        m_isSure = true;
    }
    public void DisposeComputeBuffer()
    {
        if(HizTexutreRT != null)
        {
            HizTexutreRT.Release();
            HizTexutreRT = null;
        }

        if (m_clusterBuffer != null)
        {
            m_clusterBuffer.Dispose();
            m_clusterBuffer = null;
        }

        if(m_argsBuffer != null)
        {
            m_argsBuffer.Dispose();
            m_argsBuffer = null;
        }

        if(m_resultBuffer != null)
        {
            m_resultBuffer.Dispose();
            m_resultBuffer = null;
        }

        if (m_argsShadowBuffer != null)
        {
            m_argsShadowBuffer.Dispose();
            m_argsShadowBuffer = null;
        }

        if (m_resultShadowBuffer != null)
        {
            m_resultShadowBuffer.Dispose();
            m_resultShadowBuffer = null;
        }
        m_cascadeDistances = null;
        m_isSure = false;
    }

    private ComputeBuffer GeneateResultBuffer(VegetationData assetData)
    {
        List<VegetationList> allVegetation = assetData.allObj;
        List<VegetationAsset> assetList = assetData.assetList;

        int resultNum = 0;
        for (int i = 0; i < allVegetation.Count; i++)
        {
            var vegetationList = allVegetation[i];
            VegetationAsset asset = assetList[vegetationList.assetId];

            int clusterCount = vegetationList.clusterData.Count;

            resultNum += (asset.lodAsset.Count * clusterCount);
        }

        return new ComputeBuffer(resultNum, sizeof(uint));
    }
    private void RunTimeCreateBuffer(VegetationData assetData)
    {
        List<VegetationList> allVegetation = assetData.allObj;
        List<ClusterKindData> clusterKindData = assetData.clusterKindData;
        List<VegetationAsset> assetList = assetData.assetList;

        //正常渲染args buffer
        List<uint> args = new List<uint>();
        foreach (var vegetationList in allVegetation)
        {
            VegetationAsset asset = assetList[vegetationList.assetId];
            for(int i = 0; i < asset.lodAsset.Count; i++)
            {
                var lod = asset.lodAsset[i];
                lod.buffer?.Release();
                lod.buffer = new ComputeBuffer(vegetationList.clusterData.Count, Marshal.SizeOf(typeof(InstanceBuffer)));

                lod.buffer.SetData(vegetationList.InstanceData);

                lod.materialRun = GameObject.Instantiate<Material>(lod.materialData);
                lod.materialRun.SetBuffer(HZBBufferName._InstanceBuffer, lod.buffer);

                ClusterKindData cKindData = clusterKindData[vegetationList.clusterData[0].clusterKindIndex];
                lod.materialRun.SetFloat(HZBMatParameterName._ResultOffset, cKindData.kindResultStart + vegetationList.clusterData.Count * i);
                lod.materialRun.SetFloat(HZBMatParameterName._ResultShadowOffset, cKindData.kindShadowResultStart);

                var mesh = lod.mesh;
                args.Add(mesh.GetIndexCount(0));
                args.Add(0);
                args.Add(mesh.GetIndexStart(0));
                args.Add(mesh.GetBaseVertex(0));
                args.Add(0);
            }
        }
        argsCount = args.Count / 5;
        m_argsBuffer?.Release();
        m_argsBuffer = new ComputeBuffer(argsCount, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
        m_argsBuffer.SetData(args);
        
    }

    private uint[] GetArgs(VegetationLOD lod)
    {
        var mesh = lod.mesh;
        uint[] args = new uint[5];

        args[0] = mesh.GetIndexCount(0);
        args[1] = 0;
        args[2] = mesh.GetIndexStart(0);
        args[3] = mesh.GetBaseVertex(0);
        args[4] = 0;
        return args;
    }

    public void CreateShadowRelevantBuffer(VegetationData assetData, float[] cascadeDistances)
    {
        if(cascadeDistances == null)
        {
            return;
        }
        int lastCsmCount = 0;

        if (m_cascadeDistances == null)
        {
            m_cascadeDistances = cascadeDistances;
        }
        else
        {
            lastCsmCount = m_cascadeDistances.Length;
            m_cascadeDistances = cascadeDistances;
        }
        int csmCount = m_cascadeDistances.Length;
        if (csmCount == lastCsmCount)
        {
            return;
        }
        if(csmCount > 0)
        {
            List<VegetationList> allVegetation = assetData.allObj;
            List<ClusterKindData> clusterKindData = assetData.clusterKindData;
            List<VegetationAsset> assetList = assetData.assetList;


            //3级CSM阴影的args buffer
            List<uint> argsCSM0 = new List<uint>();
            List<uint> argsCSM1 = new List<uint>();
            List<uint> argsCSM2 = new List<uint>();
            int argsIndex = 0;
            int resultNum = 0;
            foreach (var vegetationList in allVegetation)
            {
                VegetationAsset asset = assetList[vegetationList.assetId];
                if(asset.shadowLODLevel < 0)
                {
                    continue;
                }
                //用哪一级LOD作为阴影
                if(asset.lodAsset.Count > 2)
                {
                    asset.lodLevel = asset.lodAsset.Count - 2;
                    asset.lodLevelLow = asset.lodAsset.Count - 1;
                }

                var args = GetArgs(asset.lodAsset[asset.lodLevel]);

                if (csmCount > 0)
                {
                    //0级视椎体
                    argsCSM0.AddRange(args);
                }

                args = GetArgs(asset.lodAsset[asset.lodLevelLow]);
                if (csmCount > 1)
                {
                    //1级视椎体
                    argsCSM1.AddRange(args);
                }

                ClusterKindData cKindData = clusterKindData[vegetationList.clusterData[0].clusterKindIndex];
                cKindData.SetShadowData(argsIndex, resultNum);
                clusterKindData[vegetationList.clusterData[0].clusterKindIndex] = cKindData;

                resultNum += vegetationList.clusterData.Count;
                argsIndex++;
            }
            
            if (csmCount > 2)
            {
                //2级视椎体
                argsCSM2.AddRange(argsCSM1);
            }


            //阴影的argsbuffer
            m_argsShadowCount = argsCSM0.Count / 5;
            if (argsCSM0.Count == 0)
            {
                m_argsShadowBuffer?.Release();
                m_argsShadowBuffer = null;
            }
            else
            {
                m_argsShadowBuffer?.Release();
                m_argsShadowBuffer = new ComputeBuffer(m_argsShadowCount * csmCount, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
                argsCSM0.AddRange(argsCSM1);
                argsCSM0.AddRange(argsCSM2);
                m_argsShadowBuffer.SetData(argsCSM0);
            }

            //阴影的result buffer
            m_shadowClusterCount = resultNum ;
            m_resultShadowBuffer?.Release();
            m_resultShadowBuffer = new ComputeBuffer(m_shadowClusterCount * csmCount, sizeof(uint));
        }


        //种类buffer
        m_clusterKindBuffer?.Release();
        m_clusterKindBuffer = new ComputeBuffer(assetData.allObj.Count, Marshal.SizeOf(typeof(ClusterKindData)));
        m_clusterKindBuffer.SetData(assetData.clusterKindData);

    }
    #endregion

#if UNITY_EDITOR
    public void DispatchComputeDebug(UnityEngine.Rendering.CommandBuffer cmd, int hzbTestCs, ComputeShader testHZBCs)
    {
        if (m_testHZBCs == null)
        {
            m_testHZBCs = testHZBCs;
            
            m_argsDebugCs = testHZBCs.FindKernel("BakedClearArgs");
        }

        if(ClearargsDebugBuffer)
        {
            ClearargsDebugBuffer = false;

            //清除args
           /* testHZBCs.EnableKeyword("_BAKEDCODE");
            m_argsDebugCs = testHZBCs.FindKernel("BakedClearArgs");
            cmd.SetComputeIntParam(m_testHZBCs, m_csMaxSize, m_visibleArgsBuffer.count);
            cmd.SetComputeBufferParam(m_testHZBCs, m_argsDebugCs, HZBBufferName._VisibleArgsDebugBuffer, m_visibleArgsBuffer);
            cmd.DispatchCompute(m_testHZBCs, m_argsDebugCs, Mathf.CeilToInt(m_visibleArgsBuffer.count / 64.0f), 1, 1);*/
            
            if(m_ClearData != null)
            {
                m_visibleArgsBuffer.SetData(m_ClearData);
            }
        }

        if (DontRunHZBTest)
        {
            testHZBCs.DisableKeyword("_BAKEDCODE");
        }
        else
        {
            testHZBCs.EnableKeyword("_BAKEDCODE");
        }

        cmd.SetComputeBufferParam(m_testHZBCs, hzbTestCs, HZBBufferName._VisibleArgsDebugBuffer, m_visibleArgsBuffer);
    }
    public ComputeBuffer EnableDebugBuffer()
    {
        List<VegetationList> allVegetation = m_vData.allObj;
        List<VegetationAsset> assetList = m_vData.assetList;
        int argsCount = 0;

        foreach (var vegetationList in allVegetation)
        {
            VegetationAsset asset = assetList[vegetationList.assetId];
            argsCount += asset.lodAsset.Count;
        }
        m_ClearData = new uint[argsCount];
        m_visibleArgsBuffer = new ComputeBuffer(argsCount, sizeof(uint), ComputeBufferType.IndirectArguments);
        m_visibleArgsBuffer.SetData(m_ClearData);
        enableDebugBuffer = true;
        ClearargsDebugBuffer = true;
        return m_visibleArgsBuffer;
    }
    public void UnEnableDebugBuffer()
    {
        if(m_testHZBCs != null)
        {
            m_testHZBCs.DisableKeyword("_BAKEDCODE");
        }
        
        enableDebugBuffer = false;
        ClearargsDebugBuffer = false;
        m_argsDebugCs = -1;
        m_visibleArgsBuffer.Dispose();
        m_visibleArgsBuffer = null;
        m_testHZBCs = null;
        m_ClearData = null;
    }
#endif

    private static HiZGlobelManager _Instance = null;
    static public HiZGlobelManager Instance { 
        get 
        { 
            if(_Instance == null)
            {
                _Instance = new HiZGlobelManager();
            }
            return _Instance;
        }
    }
    public ComputeBuffer ClusterBuffer { get => m_clusterBuffer; }
    public ComputeBuffer ArgsBuffer { get => m_argsBuffer; }
    public ComputeBuffer ResultBuffer { get => m_resultBuffer; }
    public bool IsSure { get { return m_isSure && m_vData != null && HizTexutreRT != null; } }
    public VegetationData VData { get => m_vData; }
    public ComputeBuffer ClusterKindBuffer { get => m_clusterKindBuffer;}
    public ComputeBuffer ArgsShadowBuffer { get => m_argsShadowBuffer; set => m_argsShadowBuffer = value; }
    public ComputeBuffer ResultShadowBuffer { get => m_resultShadowBuffer; set => m_resultShadowBuffer = value; }
    public float[] CascadeDistances { get => m_cascadeDistances; set => CreateShadowRelevantBuffer(VData, value); }
    public int ArgsShadowCount { get => m_argsShadowCount; set => m_argsShadowCount = value; }
    public int ShadowClusterCount { get => m_shadowClusterCount; set => m_shadowClusterCount = value; }
}
