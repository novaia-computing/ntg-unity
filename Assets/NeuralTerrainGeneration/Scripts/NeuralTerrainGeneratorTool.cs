#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;
using UnityEditor;
using UnityEditor.TerrainTools;
using UnityEngine.TerrainTools;
using NeuralTerrainGeneration;

namespace NeuralTerrainGeneration
{
    public class NeuralTerrainGeneratorTool : TerrainPaintTool<NeuralTerrainGeneratorTool>
    {
        // General.
        private WorkerFactory.Type workerType = WorkerFactory.Type.ComputePrecompiled;
        private TensorMathHelper tensorMathHelper = new TensorMathHelper();
        private TerrainHelper terrainHelper = new TerrainHelper();
        private int modelOutputWidth = 256;
        private int modelOutputHeight = 256;
        private float heightMultiplier = 0.5f;
        private int channels = 1;
        private const string modelFolder = "Assets/NeuralTerrainGeneration/NNModels/";
        private const string modelName = "pix_diffuser_epoch62.onnx";
        private const string fullModelPath = modelFolder + modelName;
        private NNModel modelAsset;
        private Model runtimeModel;
        private HeightmapGenerator heightmapGenerator = new HeightmapGenerator();

        // Blending.
        private float radius1 = 128.0f;
        private float radius2 = 256.0f;
        private float bValue = 2.5f;
        private bool keepNeighborHeights = false;
        private NeighborBlender neighborBlender = new NeighborBlender();
        
        // Diffusion.
        private const float maxSignalRate = 0.9f;
        private const float minSignalRate = 0.02f;
        private Diffuser diffuser;
        private int samplingSteps = 10;
        private bool randomSeed = true;
        private int seed = 0;

        // Upsampling.
        // Left: upsample resolution, right: upsample factor.
        private enum UpSampleResolution 
        {
            _256 = 1, 
            _512 = 2, 
            _1024 = 4, 
            _2048 = 8, 
            _4096 = 16
        };
        private UpSampleResolution upSampleResolution = UpSampleResolution._512;
        private int upSampleFactor = 2;
        private BicbubicUpSampler bicubicUpSampler = new BicbubicUpSampler();
        private BarraUpSampler barraUpSampler;
        private enum UpSamplerType { Barracuda, Custom };
        private UpSamplerType upSamplerType = UpSamplerType.Barracuda;
        private const bool bilinearUpSampling = true;

        // Brushes.
        private bool brushesEnabled = false;
        private Texture2D brushTexture1;
        private Texture2D brushTexture2;
        private float brushOpacity = 0.5f;
        private float brushSize = 533f;
        private float brushRotation = 0f;
        private float brushHeightOffset = 0.1f;
        private bool stampMode = true;
        private bool hasPainted = false;
        private Texture2D brushMask;
        private Texture2D brushHeightmap;
        private Texture2D brushHeightmapMasked;
        private const string brushFolder = "Assets/NeuralTerrainGeneration/BrushMasks/";
        private const string defaultBrushName = "square_brush_01.png";
        private const string fullBrushPath = brushFolder + defaultBrushName;

        // Smoothing.
        private GaussianSmoother gaussianSmoother;
        private bool smoothingEnabled = true;
        private int kernelSize = 12;
        private float sigma = 6.0f;
        private int stride = 1;
        private int pad = 11;

        public override string GetName()
        {
            return "Neural Terrain Generator";
        }

        public override string GetDescription()
        {
            return "Diffusion based neural terrain generator.";
        }

        public override void OnEnable()
        {
            LoadModel();
            LoadBrushMask();
            
            upSampleFactor = (int)upSampleResolution;
            barraUpSampler = new BarraUpSampler(
                modelOutputWidth,
                modelOutputHeight,
                upSampleFactor,
                bilinearUpSampling,
                workerType
            );
            gaussianSmoother = new GaussianSmoother(
                kernelSize,
                sigma,
                stride,
                pad,
                modelOutputWidth * upSampleFactor,
                modelOutputHeight * upSampleFactor,
                workerType
            );
            diffuser = new Diffuser(workerType, runtimeModel);
        }

        public override void OnInspectorGUI(Terrain terrain, IOnInspectorGUI editContext)
        {
            GUIStyle headerStyle = EditorStyles.boldLabel;

            EditorGUILayout.LabelField("Backend", headerStyle);
            modelAsset = (NNModel)EditorGUILayout.ObjectField(
                "Model Asset", 
                modelAsset, 
                typeof(NNModel), 
                false
            );
            workerType = (WorkerFactory.Type)EditorGUILayout.EnumPopup(
                "Worker Type", 
                workerType
            );

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("General", headerStyle);
            heightMultiplier = EditorGUILayout.Slider(
                "Height Multiplier", 
                heightMultiplier,
                0.0f,
                2.0f
            );
            samplingSteps = EditorGUILayout.IntSlider(
                "Sampling Steps", 
                samplingSteps,
                5,
                20
            );


            EditorGUILayout.Space();
            EditorGUILayout.LabelField("UpSampling", headerStyle);
            upSamplerType = (UpSamplerType)EditorGUILayout.EnumPopup(
                "UpSampler Type", 
                upSamplerType
            );
            upSampleResolution = (UpSampleResolution)EditorGUILayout.EnumPopup(
                "UpSample Resolution", 
                upSampleResolution
            );
            upSampleFactor = (int)upSampleResolution;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Smoothing", headerStyle);
            smoothingEnabled = EditorGUILayout.Toggle("Smoothing Enabled", smoothingEnabled);
            if(smoothingEnabled)
            {
                kernelSize = EditorGUILayout.IntField("Kernel Size", kernelSize);
                sigma = EditorGUILayout.FloatField("Sigma", sigma);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Seeding", headerStyle);
            randomSeed = EditorGUILayout.Toggle("Random Seed", randomSeed);
            if(!randomSeed)
            {
                seed = EditorGUILayout.IntField("Seed", seed);
            }
            else
            {
                // Display seed as read only label.
                EditorGUILayout.LabelField("Seed", seed.ToString());
            }

            EditorGUILayout.Space();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Brush", headerStyle);
            BrushGUI();
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Whole Tile Generation", EditorStyles.boldLabel);
            FromScratchGUI(terrain);
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Blending", EditorStyles.boldLabel);
            BlendGUI(terrain);
        }

        private void BrushGUI()
        {
            if(!brushesEnabled)
            {
                if(GUILayout.Button("Enable Brush"))
                {
                    brushesEnabled = true;
                }
            }
            else
            {
                if(GUILayout.Button("Disable Brush"))
                {
                    brushesEnabled = false;
                }

                // Brush mask.
                EditorGUILayout.HelpBox("Brush masks must be 256x256.", MessageType.Info);
                Texture2D tempBrushMask = (Texture2D)EditorGUILayout.ObjectField(
                    "Brush Mask", 
                    brushMask, 
                    typeof(Texture2D), 
                    false
                );

                if(tempBrushMask != brushMask)
                {
                    brushMask = tempBrushMask;

                }
                EditorGUILayout.Space();

                // Brush controls.
                brushOpacity = EditorGUILayout.Slider("Opacity", brushOpacity, 0, 1);
                brushSize = EditorGUILayout.Slider("Size", brushSize, .001f, 2000f);
                brushRotation = EditorGUILayout.Slider("Rotation", brushRotation, 0, 360);
                brushHeightOffset = EditorGUILayout.Slider("Height Offset", brushHeightOffset, 0, 1);
                stampMode = EditorGUILayout.Toggle("Stamp Mode", stampMode);

                EditorGUILayout.Space();
                if(GUILayout.Button("Generate Brush Heighmap"))
                {

                }

                GUIStyle style = new GUIStyle(GUI.skin.box);
                style.fixedWidth = 256;
                style.fixedHeight = 256;
                if(brushHeightmap != null)
                {
                    EditorGUILayout.LabelField("Brush Heightmap:");
                    GUILayout.Box(brushHeightmap, style);
                }
            }
        }

        private void GenerateBrushHeightmap()
        {
            /*float[] brushHeightmapArray = GenerateHeightmap(
                upSampleResolution, 
                samplingSteps
            );

            for(int i = 0; i < brushHeightmapArray.Length; i++)
            {
                brushHeightmapArray[i] -= brushHeightOffset;
            }

            Color[] colorBrushHeightmap = new Color[brushHeightmapArray.Length];
            for(int i = 0; i < brushHeightmapArray.Length; i++)
            {
                colorBrushHeightmap[i] = new Color(
                    brushHeightmapArray[i] * heightMultiplier, 
                    brushHeightmapArray[i] * heightMultiplier, 
                    brushHeightmapArray[i] * heightMultiplier,
                    1
                );
            }

            CalculateUpSampledDimensions();
            brushHeightmap = new Texture2D(upSampledWidth, upSampledHeight);
            brushHeightmap.SetPixels(
                0, 
                0, 
                upSampledWidth, 
                upSampledHeight, 
                colorBrushHeightmap
            );
            brushHeightmap.Apply();

            // Loads default brush mask if no other mask is loaded.
            LoadBrushMask();

            GenerateMaskedBrushHeightmap();*/
        }

        private void GenerateMaskedBrushHeightmap()
        {
            /*if(brushHeightmap == null || brushMask == null) { return; }

            Tensor brushMaskTensor = new Tensor(brushMask, 1);
            int upSampleFactor = (int)upSampleResolution;
            /*BarraUpSampler barraUpSampler = new BarraUpSampler(
                modelOutputWidth,
                modelOutputHeight,
                upSampleFactor, 
                true,
                workerType
            );
            Tensor upSampledBrushMaskTensor = barraUpSampler.Execute(brushMaskTensor);*/
            /*Tensor upsampledBrushMask = UpSample(brushMaskTensor, upSampleResolution);
            Tensor smoothedBrushMask = upsampledBrushMask; //Smooth(upsampledBrushMask);
            // Consider smoothing upsample brush mask, otherwise it makes heightmap jagged.

            brushHeightmapMasked = new Texture2D(brushHeightmap.width, brushHeightmap.height);

            Color[] brushHeightmapColors = brushHeightmap.GetPixels();
            Color[] brushHeightmapMaskedColors = new Color[brushHeightmapColors.Length];

            for(int i = 0; i < smoothedBrushMask.length; i++)
            {
                brushHeightmapMaskedColors[i] = brushHeightmapColors[i] * 0;
            }

            brushMaskTensor.Dispose();
            upsampledBrushMask.Dispose();
            smoothedBrushMask.Dispose();

            brushHeightmapMasked = new Texture2D(brushHeightmap.width, brushHeightmap.height);
            brushHeightmapMasked.SetPixels(brushHeightmapMaskedColors);
            brushHeightmapMasked.Apply();

            brushHeightmapMasked = brushHeightmap;
            */
            /*Tensor secondPass = new Tensor(brushHeightmapMasked, 1);
            Tensor secondPassSmoothed = Smooth(secondPass, true);
            for(int i = 0; i < smoothedBrushMask.length; i++)
            {
                brushHeightmapMaskedColors[i] = new Color(
                    secondPassSmoothed[i],
                    secondPassSmoothed[i],
                    secondPassSmoothed[i],
                    1
                );
            }
            brushHeightmapMasked.SetPixels(brushHeightmapMaskedColors);
            brushHeightmapMasked.Apply();*/
        }

        private void FromScratchGUI(Terrain terrain)
        {
            CalculateBlendingRadii();
            if(GUILayout.Button("Generate Terrain From Scratch"))
            {
                if(randomSeed)
                {
                    seed = UnityEngine.Random.Range(0, 100000);
                }

                if(smoothingEnabled)
                {
                    gaussianSmoother.UpdateSmoother(
                        kernelSize,
                        sigma,
                        stride,
                        pad,
                        modelOutputWidth * upSampleFactor,
                        modelOutputHeight * upSampleFactor,
                        workerType
                    );
                }
                barraUpSampler.UpdateUpSampler(
                    modelOutputWidth,
                    modelOutputHeight,
                    upSampleFactor,
                    bilinearUpSampling,
                    workerType
                );
                diffuser.UpdateDiffuser(
                    workerType,
                    runtimeModel
                );

                float[] heightmap = heightmapGenerator.GenerateHeightmapFromScratch(
                    modelOutputWidth,
                    modelOutputHeight,
                    samplingSteps,
                    seed,
                    smoothingEnabled,
                    barraUpSampler,
                    gaussianSmoother,
                    diffuser
                );

                terrainHelper.SetTerrainHeights(
                    terrain, 
                    heightmap,
                    modelOutputWidth * upSampleFactor,
                    modelOutputHeight * upSampleFactor,
                    heightMultiplier
                );
            }
        }

        private void BlendGUI(Terrain terrain)
        {
            bValue = EditorGUILayout.Slider("Blend Function Start Value", bValue, 2.5f, 5.0f);
            //keepNeighborHeights = EditorGUILayout.Toggle("Keep Neighbor Heights", keepNeighborHeights);

            if(GUILayout.Button("Blend With Neighbors"))
            {
                neighborBlender.BlendAllNeighbors(
                    terrain, 
                    modelOutputWidth * upSampleFactor, 
                    modelOutputHeight * upSampleFactor, 
                    radius1, 
                    radius2, 
                    bValue, 
                    keepNeighborHeights
                );
            } 
        }

        private void RenderIntoPaintContext(
            PaintContext paintContext, 
            Texture brushTexture, 
            BrushTransform brushXform
        )
        {
            // Get the built-in painting Material reference.
            Material mat = TerrainPaintUtility.GetBuiltinPaintMaterial();
            
            // Bind the current brush texture.
            mat.SetTexture("_BrushTex", brushTexture);

            // Bind the tool-specific shader properties.
            var opacity = Event.current.control ? -brushOpacity : brushOpacity;
            mat.SetVector("_BrushParams", new Vector4(opacity, 0.0f, 0.0f, 0.0f));

            // Setup the material for reading from/writing into the PaintContext texture data. 
            // This is a necessary step to setup the correct shader properties for 
            // appropriately transforming UVs and sampling textures within the shader.
            TerrainPaintUtility.SetupTerrainToolMaterialProperties(
                paintContext, 
                brushXform, 
                mat
            );
            
            // Render into the PaintContext's destinationRenderTexture using 
            // the built-in painting Material - the id for the Raise/Lower pass is 0.
            Graphics.Blit(
                paintContext.sourceRenderTexture, 
                paintContext.destinationRenderTexture, 
                mat, 
                0
            );
        }

        // Render Tool previews in the SceneView
        public override void OnRenderBrushPreview(Terrain terrain, IOnSceneGUI editContext)
        {
            // Don't do anything if brushes are disabled.
            if(!brushesEnabled) { return; }

            // Dont render preview if this isnt a Repaint.
            if(Event.current.type != EventType.Repaint) { return; }

            // Only do the rest if user mouse hits valid terrain.
            if(!editContext.hitValidTerrain) { return; }

            // Get the current BrushTransform under the mouse position relative to the Terrain.
            BrushTransform brushXform = TerrainPaintUtility.CalculateBrushTransform(
                terrain, 
                editContext.raycastHit.textureCoord, 
                brushSize, 
                brushRotation
            );

            // Get the PaintContext for the current BrushTransform. 
            // This has a sourceRenderTexture from which to read existing Terrain texture data.
            PaintContext paintContext = TerrainPaintUtility.BeginPaintHeightmap(
                terrain, 
                brushXform.GetBrushXYBounds(), 
                1
            );

            // Get the built-in Material for rendering Brush Previews
            Material previewMaterial = TerrainPaintUtilityEditor.GetDefaultBrushPreviewMaterial();

            // Render the brush preview for the sourceRenderTexture. 
            // This will show up as a projected brush mesh rendered on top of the Terrain
            TerrainPaintUtilityEditor.DrawBrushPreview(
                paintContext, 
                TerrainBrushPreviewMode.SourceRenderTexture, 
                brushHeightmapMasked, 
                brushXform, 
                previewMaterial, 
                0
            );

            // Render changes into the PaintContext destinationRenderTexture
            RenderIntoPaintContext(paintContext, brushHeightmapMasked, brushXform);

            // Restore old render target.
            RenderTexture.active = paintContext.oldRenderTexture;

            // Bind the sourceRenderTexture to the preview Material. This is used to compute deltas in height
            previewMaterial.SetTexture("_HeightmapOrig", paintContext.sourceRenderTexture);

            // Render a procedural mesh displaying the delta/displacement in height from the source Terrain texture data. 
            // When modifying Terrain height, this shows how much the next paint operation will alter the Terrain height.
            TerrainPaintUtilityEditor.DrawBrushPreview(
                paintContext, 
                TerrainBrushPreviewMode.DestinationRenderTexture, 
                brushHeightmapMasked, 
                brushXform, 
                previewMaterial, 
                1
            );

            // Cleanup resources
            TerrainPaintUtility.ReleaseContextResources(paintContext);
        }

        // Perform painting operations that modify the Terrain texture data.
        public override bool OnPaint(Terrain terrain, IOnPaint editContext)
        {
            if(!brushesEnabled) { return false; }
            if(stampMode)
            {
                if(hasPainted) { return false; }
                hasPainted = true;
            }

            // Get the current BrushTransform under the mouse position relative to the Terrain
            BrushTransform brushXform = TerrainPaintUtility.CalculateBrushTransform(
                terrain, 
                editContext.uv, 
                brushSize, 
                brushRotation
            );

            // Get the PaintContext for the current BrushTransform. 
            // This has a sourceRenderTexture from which to read existing Terrain texture data
            // and a destinationRenderTexture into which to write new Terrain texture data
            PaintContext paintContext = TerrainPaintUtility.BeginPaintHeightmap(
                terrain, 
                brushXform.GetBrushXYBounds()
            );

            // Call the common rendering function used by OnRenderBrushPreview and OnPaint
            RenderIntoPaintContext(paintContext, brushHeightmapMasked, brushXform);

            // Commit the modified PaintContext with a provided string for tracking Undo operations. 
            // This function handles Undo and resource cleanup for you.
            TerrainPaintUtility.EndPaintHeightmap(
                paintContext, 
                "Terrain Paint - Raise or Lower Height"
            );

            // Return whether or not Trees and Details should be hidden while painting with this Terrain Tool
            return true;
        }

        public override void OnSceneGUI(Terrain terrain, IOnSceneGUI editContext)
        {
            Event current = Event.current;
            switch(current.type)
            {
                // Keep track of when mouse has been released in order to determine if user can paint in stamp mode.
                case EventType.MouseUp:
                    hasPainted = false;
                    break;
            }
        }

        private void CalculateBlendingRadii()
        {
            int upSampledWidth = modelOutputWidth * upSampleFactor;
            radius1 = upSampledWidth / 2;
            radius2 = upSampledWidth;
        }

        // Returns true when model loaded succesfully, false otherwise.
        private bool LoadModel()
        {
            if(modelAsset == null)
            {
                modelAsset = (NNModel)AssetDatabase.LoadAssetAtPath(
                    fullModelPath, 
                    typeof(NNModel)
                );
            }
            if(modelAsset != null)
            {
                runtimeModel = ModelLoader.Load(modelAsset);
            }
            else
            {
                Debug.LogError("Model asset is null.");
                return false;
            }

            return true;
        }

        private void LoadBrushMask()
        {
            if(brushMask == null)
            {
                Debug.Log("Loading brush mask.");
                brushMask = (Texture2D)AssetDatabase.LoadAssetAtPath(
                    fullBrushPath, 
                    typeof(Texture2D)
                );
            }
        }
    }
}

#endif