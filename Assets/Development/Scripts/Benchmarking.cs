using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;
using System;

public class Benchmarking : MonoBehaviour
{
    [SerializeField] private int numTrials = 100;
    
    public void NormalAdd(Tensor tensor1, Tensor tensor2)
    {
        Tensor newTensor = new Tensor(tensor1.batch, tensor1.height, tensor1.width, tensor1.channels);
        for(int i = 0; i < tensor1.length; i++)
        {
            newTensor[i] = tensor1[i] + tensor2[i];
        }

        tensor1.Dispose();
        tensor2.Dispose();
        newTensor.Dispose();
    }

    private void BarraAdd(Tensor tensor1, Tensor tensor2)
    {
        ModelBuilder builder = new ModelBuilder();
        object[] inputs = new object[] 
        { 
            builder.Const("tensor1", tensor1).name,
            builder.Const("tensor2", tensor2).name 
        };
        Layer addLayer = builder.Add("Add", inputs);
        builder.Output(addLayer);
        Model model = builder.model;

        IWorker worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);
        worker.Execute();
        Tensor output = worker.PeekOutput();

        tensor1.Dispose();
        tensor2.Dispose();
        output.Dispose();
        worker.Dispose();
    }

    public void BarraAddBenchmark()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for(int i = 0; i < numTrials; i++)
        {
            BarraAdd(
                PopulatedTensor(2, 100, 100),
                PopulatedTensor(1, 100, 100)
            );
        }
        watch.Stop();
        Debug.Log("BarraAdd: " + watch.ElapsedMilliseconds + "ms");
    }

    public void NormalAddBenchmark()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for(int i = 0; i < numTrials; i++)
        {
            NormalAdd(
                PopulatedTensor(2, 100, 100),
                PopulatedTensor(1, 100, 100)
            );
        }
        watch.Stop();
        Debug.Log("NormalAdd: " + watch.ElapsedMilliseconds + "ms");
    }

    private void BarraMul(Tensor tensor1, Tensor tensor2)
    {
        ModelBuilder builder = new ModelBuilder();
        object[] inputs = new object[] 
        { 
            builder.Const("tensor1", tensor1).name,
            builder.Const("tensor2", tensor2).name 
        };
        Layer mulLayer = builder.Mul("Mul", inputs);
        builder.Output(mulLayer);
        Model model = builder.model;

        IWorker worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);
        worker.Execute();
        Tensor output = worker.PeekOutput();

        tensor1.Dispose();
        tensor2.Dispose();
        output.Dispose();
        worker.Dispose();
    }

    private void NormalMul(Tensor tensor1, Tensor tensor2)
    {
        Tensor newTensor = new Tensor(tensor1.batch, tensor1.height, tensor1.width, tensor1.channels);
        for(int i = 0; i < tensor1.length; i++)
        {
            newTensor[i] = tensor1[i] * tensor2[i];
        }

        tensor1.Dispose();
        tensor2.Dispose();
        newTensor.Dispose();
    }

    private void BurstMul(Tensor tensor1, Tensor tensor2, BurstCPUOps ops)
    {
        Tensor[] tensors = new Tensor[] { tensor1, tensor2 };
        Tensor output = ops.Mul(tensors);
        tensor1.Dispose();
        tensor2.Dispose();
        output.Dispose();
    }

    private void UnsafeMul(Tensor tensor1, Tensor tensor2, UnsafeArrayCPUOps ops)
    {
        Tensor[] tensors = new Tensor[] { tensor1, tensor2 };
        Tensor output = ops.Mul(tensors);
        tensor1.Dispose();
        tensor2.Dispose();
        output.Dispose();
    }

    public void BarraMulBenchmark()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for(int i = 0; i < numTrials; i++)
        {
            BarraMul(
                PopulatedTensor(2, 100, 100),
                PopulatedTensor(3, 100, 100)
            );
        }
        watch.Stop();
        Debug.Log("BarraMul: " + watch.ElapsedMilliseconds + "ms");
    }

    public void NormalMulBenchmark()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for(int i = 0; i < numTrials; i++)
        {
            NormalMul(
                PopulatedTensor(2, 100, 100),
                PopulatedTensor(3, 100, 100)
            );
        }
        watch.Stop();
        Debug.Log("NormalMul: " + watch.ElapsedMilliseconds + "ms");
    }

    public void BurstMulBenchmark()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        BurstCPUOps ops = new BurstCPUOps();
        for(int i = 0; i < numTrials; i++)
        {
            BurstMul(
                PopulatedTensor(2, 100, 100),
                PopulatedTensor(3, 100, 100),
                ops
            );
        }
        watch.Stop();
        Debug.Log("BurstMul: " + watch.ElapsedMilliseconds + "ms");
    }

    public void UnsafeMulBenchmark()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        UnsafeArrayCPUOps ops = new UnsafeArrayCPUOps();
        for(int i = 0; i < numTrials; i++)
        {
            UnsafeMul(
                PopulatedTensor(2, 100, 100),
                PopulatedTensor(3, 100, 100),
                ops
            );
        }
        watch.Stop();
        Debug.Log("UnsafeMul: " + watch.ElapsedMilliseconds + "ms");
    }
    
    private void BarraUpsample(Tensor tensor)
    {
        ModelBuilder builder = new ModelBuilder();
        object upsampleInput = builder.Const("input1", tensor).name;
        Int32[] upsampleScale = new Int32[] { 2, 2 };
        Layer upsampleLayer = builder.Upsample2D("Upsample2D", upsampleInput, upsampleScale, true);
        builder.Output(upsampleLayer);
        Model model = builder.model;

        IWorker worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);
        worker.Execute();
        Tensor output = worker.PeekOutput();

        tensor.Dispose();
        output.Dispose();
        worker.Dispose();
    }

    public void BarraUpsampleBenchmark()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for(int i = 0; i < numTrials; i++)
        {
            BarraUpsample(
                PopulatedTensor(2, 256, 256)
            );
        }
        watch.Stop();
        Debug.Log("BarraUpsample: " + watch.ElapsedMilliseconds + "ms");
    }

    public Tensor PopulatedTensor(float element, int width, int height)
    {
        Tensor populatedTensor = new Tensor(1, width, height, 1);
        for(int x = 0; x < width; x++)
        {
            for(int y = 0; y < height; y ++)
            {
                populatedTensor[0, x, y, 0] = element;
            }
        }
        return populatedTensor;
    }
}
