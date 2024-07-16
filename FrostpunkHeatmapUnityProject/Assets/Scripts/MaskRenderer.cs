using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SocialPlatforms;

/// <summary>
/// Component that controls the compute shader and assigns the necessary variables
/// </summary>
public class MaskRenderer : MonoBehaviour
{
    private static List<Building> buildings;

    /// <summary>
    /// 每栋建筑在启动时都会使用此功能进行自我注册
    /// 我真的不会在大型游戏项目中这样做，但它
    /// 易于扩展，因此非常适合教程
    /// </summary>
    /// <param name="building">The building to add to the list</param>
    public static void RegisterBuilding(Building building)
    {
        buildings.Add(building);
    }

    //Properties

    /// <summary>
    /// 用于渲染遮罩的ComputeShader
    /// </summary>
    [SerializeField]
    private ComputeShader computeShader = null;

    /// <summary>
    /// mask的大小
    /// 理想情况下，这是 2 的幂。
    /// </summary>
    [Range(64, 4096)]
    [SerializeField]
    private int TextureSize = 2048;

    /// <summary>
    /// The size of the map in actual units
    /// </summary>
    [SerializeField]
    private float MapSize = 0;

    [SerializeField]
    private float BlendDistance = 4.0f;

    /// <summary>
    /// 最冷温度所用的颜色
    /// </summary>
    public Color MaskColor0;
    /// <summary>
    /// 第二冷温度所用的颜色
    /// </summary>
    public Color MaskColor1;
    /// <summary>
    /// 第二高温度所用的颜色
    /// </summary>
    public Color MaskColor2;
    /// <summary>
    /// 最热温度所用的颜色
    /// </summary>
    public Color MaskColor3;
    /// <summary>
    /// Perlin 噪声纹理
    /// </summary>
    public Texture2D NoiseTexture;
    /// <summary>
    /// 采样噪声纹理时使用的 UV 缩放
    /// </summary>
    [Range(0.0f, 5.0f)]
    public float NoiseDetail = 4.0f;

    private RenderTexture maskTexture;

    //存储这些属性，以便我们可以避免在更新中查找字符串
    private static readonly int textureSizeId = Shader.PropertyToID("_TextureSize");
    private static readonly int buildingCountId = Shader.PropertyToID("_BuildingCount");
    private static readonly int mapSizeId = Shader.PropertyToID("_MapSize");
    private static readonly int blendId = Shader.PropertyToID("_Blend");

    private static readonly int color0Id = Shader.PropertyToID("_Color0");
    private static readonly int color1Id = Shader.PropertyToID("_Color1");
    private static readonly int color2Id = Shader.PropertyToID("_Color2");
    private static readonly int color3Id = Shader.PropertyToID("_Color3");

    private static readonly int noiseTexId = Shader.PropertyToID("_NoiseTex");
    private static readonly int noiseDetailId = Shader.PropertyToID("_NoiseDetail");

    private static readonly int maskTextureId = Shader.PropertyToID("_Mask");

    private static readonly int buildingBufferId = Shader.PropertyToID("_BuildingBuffer");

    /// <summary>
    /// 建筑物Buffer数据结构，包含xy位置和温度及范围
    /// </summary>
    private struct BuildingBufferElement
    {
        public float PositionX;
        public float PositionY;
        public float Range;
        public float Heat;
    }

    private List<BuildingBufferElement> bufferElements;
    private ComputeBuffer buffer = null;

    /// <summary>
    /// Initialization
    /// </summary>
    private void Awake()
    {
        //这里Awake实例化，便于Building.cs在Start中使用
        buildings = new List<Building>();

        //Create a new render texture for the mask
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        maskTexture = new RenderTexture(TextureSize, TextureSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) 
#else
        maskTexture = new RenderTexture(TextureSize, TextureSize, 0, RenderTextureFormat.ARGB32)
#endif
        { 
            enableRandomWrite = true 
        };
        maskTexture.Create();

        //Set the texture dimension and the mask texture in the compute shader
        computeShader.SetInt(textureSizeId, TextureSize);
        computeShader.SetTexture(0, maskTextureId, maskTexture);

        //Set the blend distance
        computeShader.SetFloat(blendId, BlendDistance);

        //Set the mask colors
        computeShader.SetVector(color0Id, MaskColor0);
        computeShader.SetVector(color1Id, MaskColor1);
        computeShader.SetVector(color2Id, MaskColor2);
        computeShader.SetVector(color3Id, MaskColor3);

        //Set the noise texture
        computeShader.SetTexture(0, noiseTexId, NoiseTexture);
        computeShader.SetFloat(noiseDetailId, NoiseDetail);

        //我们在多种材质中使用遮罩纹理和贴图大小
        //在这种情况下，将其设置为全局变量更容易
        //对于全尺寸游戏，应在特定材质中设置，而不是全局设置
        Shader.SetGlobalTexture(maskTextureId, maskTexture);
        Shader.SetGlobalFloat(mapSizeId, MapSize);

        bufferElements = new List<BuildingBufferElement>();
    }

    /// <summary>
    /// Cleanup
    /// </summary>
    private void OnDestroy()
    {
        buffer?.Dispose();

        if (maskTexture != null)
            DestroyImmediate(maskTexture);
    }

    //Setup all buffers and variables
    private void Update()
    {
        //Recreate the buffer
        bufferElements.Clear();
        foreach (Building building in buildings)
        {
            BuildingBufferElement element = new BuildingBufferElement
            {
                PositionX = building.transform.position.x,
                PositionY = building.transform.position.z,
                Range = building.Range,
                Heat = building.Heat
            };

            bufferElements.Add(element);
        }

        buffer?.Release();
        buffer = new ComputeBuffer(bufferElements.Count * 4, sizeof(float));

        //设置建筑的Buffer数据将其赋予ComputeShader
        buffer.SetData(bufferElements);
        computeShader.SetBuffer(0, buildingBufferId, buffer);

        //设置Building Buffer的数量
        computeShader.SetInt(buildingCountId, bufferElements.Count);

        //Execute the compute shader
        //Our thread group size is 8x8=64, 
        //thus we have to dispatch (TextureSize / 8) * (TextureSize / 8) thread groups
        computeShader.Dispatch(0, Mathf.CeilToInt(TextureSize / 8.0f), Mathf.CeilToInt(TextureSize / 8.0f), 1);
    }
}
