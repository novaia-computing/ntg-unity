using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Unity.Barracuda;

public class BaseTerrainGenerator : MonoBehaviour
{
    [SerializeField] protected NNModel modelAsset;
    protected Model runtimeModel;

    protected TensorMathHelper tensorMathHelper = new TensorMathHelper();

    [SerializeField] protected int modelOutputWidth = 256;
    [SerializeField] protected int modelOutputHeight = 256;
    protected int modelOutputArea;
    protected int channels = 1;

    [SerializeField] protected Terrain terrain;
    [SerializeField] protected float heightMultiplier = 10.0f;

    protected delegate Tensor WorkerExecuter(IWorker worker, params object[] args);

    public void Setup()
    {
        modelOutputArea = modelOutputWidth * modelOutputHeight;
        runtimeModel = ModelLoader.Load(modelAsset);
    }

    protected virtual void Start()
    {
        Setup();
    }

    public void SetTerrainHeights(Single[] heightmap)
    {
        float[,] newHeightmap = new float[modelOutputWidth, modelOutputHeight];
        for(int i = 0; i < modelOutputArea; i++)
        {
            int x = (int)(i % modelOutputWidth);
            int y = (int)Math.Floor((double)(i / modelOutputWidth));
            newHeightmap[x, y] = (float)heightmap[i] * heightMultiplier;
        }

        terrain.terrainData.SetHeights(0, 0, newHeightmap);
    }

    protected Single[] GenerateHeightmap(Model model, WorkerExecuter workerExecuter, params object[] args)
    {
        // Using ComputePrecompiled worker type for most efficient computation on GPU.
        // Reference: https://docs.unity3d.com/Packages/com.unity.barracuda@1.0/manual/Worker.html
        var worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);

        Tensor output = workerExecuter(worker, args);
        Single[] outputArray = output.ToReadOnlyArray();

        output.Dispose();
        worker.Dispose();

        return outputArray;
    }
    
    protected virtual Tensor DefaultWorkerExecuter(IWorker worker, params object[] args)
    {
        return new Tensor(1, modelOutputWidth, modelOutputHeight, 1);
    }
}