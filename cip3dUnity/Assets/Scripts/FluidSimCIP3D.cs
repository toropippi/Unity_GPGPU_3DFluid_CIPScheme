using UnityEngine;
using System.Runtime.InteropServices;
using UnityEngine.UI;
using Const;

/// <summary>
/// 沢山の弾を管理するクラス
/// </summary>
public class FluidSimCIP3D : MonoBehaviour
{
    //下記のは直接shaderに書き込み
    //const float DT = 1.00f;//デルタタイム
    public ComputeShader NSComputeShader;

    int kernelVeloc0;
    int kernelVeloc1;
    int kernelVorticity;
    int kernelcomputebuffermemcopy_i;
    int kernelcomputebuffermemcopy_f;
    int kernelAdvectionCIP;
    int kernelNewgrad_x;
    int kernelNewgrad_y;
    int kernelNewgrad_z;
    int kernelNewgrad_t;
    int kernelpressure0;
    int kernelpressure1;
    int kerneldiv;
    int kernelrhs;
    int kernelparticle;
    int kernelexforce0;
    int kernelray0;

    ComputeBuffer YU;//速度値
    ComputeBuffer YUN;//速度値
    ComputeBuffer YV;//速度値
    ComputeBuffer YVN;//速度値
    ComputeBuffer YW;//速度値
    ComputeBuffer YWN;//速度値
    ComputeBuffer TP;//温度
    ComputeBuffer TPN;//温度

    ComputeBuffer YUT;//U速度平均
    ComputeBuffer YVT;//V速度平均
    ComputeBuffer YWT;//W速度平均

    ComputeBuffer YUV;//U速度平均
    ComputeBuffer YUW;//U速度平均
    ComputeBuffer YWU;//W速度平均
    ComputeBuffer YWV;//W速度平均
    ComputeBuffer YVU;//V速度平均
    ComputeBuffer YVW;//V速度平均

    ComputeBuffer GXU;//u速度x偏微分
    ComputeBuffer GYU;//u速度y偏微分
    ComputeBuffer GZU;//u速度z偏微分
    ComputeBuffer GXV;//v速度x偏微分
    ComputeBuffer GYV;//v速度y偏微分
    ComputeBuffer GZV;//v速度z偏微分
    ComputeBuffer GXW;//w速度x偏微分
    ComputeBuffer GYW;//w速度y偏微分
    ComputeBuffer GZW;//w速度z偏微分
    ComputeBuffer GXTP;//TPx偏微分
    ComputeBuffer GYTP;//TPy偏微分
    ComputeBuffer GZTP;//TPz偏微分

    ComputeBuffer GXU_;//u速度x偏微分
    ComputeBuffer GYU_;//u速度y偏微分
    ComputeBuffer GZU_;//u速度z偏微分
    ComputeBuffer GXV_;//v速度x偏微分
    ComputeBuffer GYV_;//v速度y偏微分
    ComputeBuffer GZV_;//v速度z偏微分
    ComputeBuffer GXW_;//w速度x偏微分
    ComputeBuffer GYW_;//w速度y偏微分
    ComputeBuffer GZW_;//w速度z偏微分
    ComputeBuffer GXTP_;//TPx偏微分
    ComputeBuffer GYTP_;//TPy偏微分
    ComputeBuffer GZTP_;//TPz偏微分

    //座標関係ないスカラー
    ComputeBuffer VOR;
    ComputeBuffer YPN;
    ComputeBuffer DIV;

    ComputeBuffer WallX;
    ComputeBuffer WallY;
    ComputeBuffer WallZ;
    ComputeBuffer WallP;
    //その他
    public ComputeBuffer ParticlePos;//描画でも使う
    //レイマーチング関連
    RenderTexture rt;//出力画像
    [SerializeField] CameraDir cameraDir;


    int cnt = 0;
    void Awake()
    {
        //RGBA floatで作成
        rt = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGBFloat);
        rt.enableRandomWrite = true;
        rt.Create();
        var rawimage = GameObject.Find("RawImage").GetComponent<RawImage>();
        rawimage.texture = rt;
    }
    void Start()
    {
        //GPUメモリ確保
        FindKernelInit();
        InitializeComputeBuffer();
        SetKernels();
        SetWall();
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    void Update()
    {
        //この時点でYUN,YVN,YWNだけが最新
        int loopnum = 2;
        if (Input.GetKey(KeyCode.Space))
        {
            loopnum = 8;
        }

        for (int loopf = 0; loopf < loopnum; loopf++)
        {
            //移流フェーズ
            CopyBufferToBuffer_f(YU, YUN);
            CopyBufferToBuffer_f(YV, YVN);
            CopyBufferToBuffer_f(YW, YWN);
            if (CO.isTEMPERATURE)
                CopyBufferToBuffer_f(TP, TPN);
            Veloc();
            CIP();
            CopyBufferToBuffer_f(YU, YUN);
            CopyBufferToBuffer_f(YV, YVN);
            CopyBufferToBuffer_f(YW, YWN);
            if (CO.isTEMPERATURE)
                CopyBufferToBuffer_f(TP, TPN);

            //非移流フェーズ
            if (cnt < 9992) 
            {
                //tmpval0を設定
                if (cnt < 440)
                {
                    NSComputeShader.SetFloat("tmpval0", 1.1f);
                }
                else
                {
                    NSComputeShader.SetFloat("tmpval0", 0);
                }
                    
                Exforce();
            }
                
            Div();
            Pressure();
            Rhs();
            Newgrad();

            if (CO.isPARTICLE)
                Particle_Move();
            cnt++;
        }

        //rtにレイマーチング結果を書き込む
        if (cnt % 4 == 0)
            Ray0();
        //最後にガス抜き。毎フレームやらなくてもいいかも
        if ((cnt/2) % 256 == 255)
            GasReleasing();

        //キャプチャ
        /*
        if (cnt%1==0)
            GetComponent<Capture>().Capturef();
        */
    }



    void Ray0() 
    {
        //cameraDirの関数を使ってセット
        Vector3 campos = cameraDir.GetCameraPos();
        Vector3 camdir = cameraDir.GetCameraDir();
        Vector3 camrightdir = cameraDir.GetCameraRight();
        Vector3 camupdir = cameraDir.GetCameraUp();

        NSComputeShader.SetVector("campos", new Vector4(campos.x, campos.y, campos.z, 0.0f));
        NSComputeShader.SetVector("camdir", new Vector4(camdir.x, camdir.y, camdir.z, 0.0f));
        NSComputeShader.SetVector("camrightdir", new Vector4(camrightdir.x, camrightdir.y, camrightdir.z, 0.0f));
        NSComputeShader.SetVector("camupdir", new Vector4(camupdir.x, camupdir.y, camupdir.z, 0.0f));
        NSComputeShader.Dispatch(kernelray0, rt.width / 8, rt.height / 8, 1);
    }




    //圧力の平均値がどんどん高くなっている場合に平均が0になるような処理を
    void GasReleasing()
    {
        //面倒なのでCPUで全部やってる
        float[] cpudiv = new float[CO.WX * CO.WY * CO.WZ];
        YPN.GetData(cpudiv);
        float gk = 0.0f;
        for (int i = 0; i < CO.WXYZ; i++)
        {
            gk += cpudiv[i];
        }
        //Debug.Log(gk);
        gk /= CO.WXYZ;

        for (int i = 0; i < CO.WXYZ; i++)
        {
            cpudiv[i] -= gk;
        }
        YPN.SetData(cpudiv);


        //念のためdivergenceを計算して表示
        if ((cnt % 120) == 0)
        {
            Div();
            cpudiv = new float[CO.WX * CO.WY * CO.WZ];
            DIV.GetData(cpudiv);
            gk = 0.0f;
            for (int i = 0; i < CO.WXYZ; i++)
            {
                gk += cpudiv[i] * cpudiv[i];
            }
            Debug.Log(Mathf.Sqrt(gk));
        }
    }



    void FindKernelInit()
    {
        kernelVeloc0 = NSComputeShader.FindKernel("Veloc0");
        kernelVeloc1 = NSComputeShader.FindKernel("Veloc1");
        kernelAdvectionCIP = NSComputeShader.FindKernel("AdvectionCIP");
        kernelNewgrad_x = NSComputeShader.FindKernel("Newgrad_x");
        kernelNewgrad_y = NSComputeShader.FindKernel("Newgrad_y");
        kernelNewgrad_z = NSComputeShader.FindKernel("Newgrad_z");
        kernelNewgrad_t = NSComputeShader.FindKernel("Newgrad_t");
        kernelpressure0 = NSComputeShader.FindKernel("pressure0");
        kernelpressure1 = NSComputeShader.FindKernel("pressure1");
        kerneldiv = NSComputeShader.FindKernel("div");
        kernelrhs = NSComputeShader.FindKernel("rhs");
        kernelVorticity = NSComputeShader.FindKernel("Vorticity");
        kernelparticle = NSComputeShader.FindKernel("particle_move");
        kernelexforce0 = NSComputeShader.FindKernel("exforce0");
        kernelray0 = NSComputeShader.FindKernel("ray0");

        kernelcomputebuffermemcopy_i = NSComputeShader.FindKernel("ComputeBufferMemcopy_i");
        kernelcomputebuffermemcopy_f = NSComputeShader.FindKernel("ComputeBufferMemcopy_f");
    }

    /// コンピュートバッファの初期化
    void InitializeComputeBuffer()
    {
        YU = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        YUN = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        YV = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        YVN = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        YW = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        YWN = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        TP = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        TPN = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));

        YUT = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        YVT = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        YWT = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));

        YUV = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        YUW = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        YWU = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        YWV = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        YVU = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        YVW = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));

        GXU = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GYU = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GZU = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GXV = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GYV = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GZV = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GXW = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GYW = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GZW = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GXTP = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GYTP = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GZTP = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GXU_ = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GYU_ = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GZU_ = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GXV_ = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GYV_ = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GZV_ = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GXW_ = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GYW_ = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GZW_ = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GXTP_ = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GYTP_ = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        GZTP_ = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));

        //座標関係ないスカラー系
        VOR = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        YPN = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));
        DIV = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(float)));

        WallX = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(uint)));
        WallY = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(uint)));
        WallZ = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(uint)));
        WallP = new ComputeBuffer(CO.WXYZ, Marshal.SizeOf(typeof(uint)));


        // 弾数
        if (CO.isPARTICLE)
        {
            ParticlePos = new ComputeBuffer(CO.PARTICLENUM, Marshal.SizeOf(typeof(Vector3)));

            // 配列に初期値を代入する
            Vector3[] bullets = new Vector3[ParticlePos.count];
            for (int i = 0; i < ParticlePos.count; i++)
            {
                if (i % 256 < 32)
                {
                    bullets[i] =
                        new Vector3(
                            Random.Range(9.0f, 11.0f), Random.Range(14.0f, 17.0f),
                            Random.Range(14.0f, 17.0f));
                }
                else
                {
                    bullets[i] =
                        new Vector3(
                            Random.Range(21.0f, 21.7f), Random.Range(14.0f, 17.0f),
                            Random.Range(14.0f, 17.0f));
                }
            }
            // 代入
            ParticlePos.SetData(bullets);
        }


        FillComputeBuffer();
    }

    void FillComputeBuffer()
    {
        FillBuffer_F(YUN);
        FillBuffer_F(YVN);
        FillBuffer_F(YWN);
        FillBuffer_F(TPN);
        FillBuffer_F(VOR);

        FillBuffer_F(GXU);
        FillBuffer_F(GYU);
        FillBuffer_F(GZU);
        FillBuffer_F(GXV);
        FillBuffer_F(GYV);
        FillBuffer_F(GZV);
        FillBuffer_F(GXW);
        FillBuffer_F(GYW);
        FillBuffer_F(GZW);
        FillBuffer_F(GXTP);
        FillBuffer_F(GYTP);
        FillBuffer_F(GZTP);

        FillBuffer_F(YPN);
        FillBuffer_F(DIV);

        FillBuffer_UI(WallX, 255);
        FillBuffer_UI(WallY, 255);
        FillBuffer_UI(WallZ, 255);
        FillBuffer_UI(WallP, 255);
    }

    void SetKernels()
    {
        //heatmapMaterial.SetBuffer("VOR", VOR);//ここで描画側のshaderとcompute bufferのVORを紐づけ
        //NSComputeShader.SetFloat("DT", DT);
        //NSComputeShader.SetFloat("MU", MU);

        NSComputeShader.SetBuffer(kernelVeloc0, "YU", YU);
        NSComputeShader.SetBuffer(kernelVeloc0, "YV", YV);
        NSComputeShader.SetBuffer(kernelVeloc0, "YW", YW);
        NSComputeShader.SetBuffer(kernelVeloc0, "YUT", YUT);
        NSComputeShader.SetBuffer(kernelVeloc0, "YVT", YVT);
        NSComputeShader.SetBuffer(kernelVeloc0, "YWT", YWT);

        NSComputeShader.SetBuffer(kernelVeloc1, "YUV", YUV);
        NSComputeShader.SetBuffer(kernelVeloc1, "YUW", YUW);
        NSComputeShader.SetBuffer(kernelVeloc1, "YVU", YVU);
        NSComputeShader.SetBuffer(kernelVeloc1, "YVW", YVW);
        NSComputeShader.SetBuffer(kernelVeloc1, "YWU", YWU);
        NSComputeShader.SetBuffer(kernelVeloc1, "YWV", YWV);
        NSComputeShader.SetBuffer(kernelVeloc1, "refYUT", YUT);
        NSComputeShader.SetBuffer(kernelVeloc1, "refYVT", YVT);
        NSComputeShader.SetBuffer(kernelVeloc1, "refYWT", YWT);

        ////////////////////CIP
        //全部dispatchのとこでやる
        ////////////////////ここまで


        ////////////////////newgrad
        //U方向関連
        NSComputeShader.SetBuffer(kernelNewgrad_x, "GX", GXU);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "GY", GYU);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "GZ", GZU);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "yn", YUN);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "y", YU);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "u", YU);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "Wall", WallX);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "dux", GXU_);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "duy", GYU_);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "duz", GZU_);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "dvx", GXV_);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "dvy", GYV_);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "dvz", GZV_);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "dwx", GXW_);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "dwy", GYW_);
        NSComputeShader.SetBuffer(kernelNewgrad_x, "dwz", GZW_);

        //V方向関連
        NSComputeShader.SetBuffer(kernelNewgrad_y, "GX", GXV);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "GY", GYV);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "GZ", GZV);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "yn", YVN);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "y", YV);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "v", YV);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "Wall", WallY);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "dux", GXU_);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "duy", GYU_);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "duz", GZU_);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "dvx", GXV_);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "dvy", GYV_);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "dvz", GZV_);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "dwx", GXW_);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "dwy", GYW_);
        NSComputeShader.SetBuffer(kernelNewgrad_y, "dwz", GZW_);

        //W方向関連
        NSComputeShader.SetBuffer(kernelNewgrad_z, "GX", GXW);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "GY", GYW);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "GZ", GZW);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "yn", YWN);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "y", YW);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "w", YW);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "Wall", WallZ);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "dux", GXU_);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "duy", GYU_);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "duz", GZU_);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "dvx", GXV_);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "dvy", GYV_);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "dvz", GZV_);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "dwx", GXW_);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "dwy", GYW_);
        NSComputeShader.SetBuffer(kernelNewgrad_z, "dwz", GZW_);

        //温度関連
        NSComputeShader.SetBuffer(kernelNewgrad_t, "GX", GXTP);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "GY", GYTP);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "GZ", GZTP);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "yn", TPN);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "y", TP);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "Wall", WallP);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "dux", GXU_);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "duy", GYU_);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "duz", GZU_);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "dvx", GXV_);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "dvy", GYV_);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "dvz", GZV_);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "dwx", GXW_);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "dwy", GYW_);
        NSComputeShader.SetBuffer(kernelNewgrad_t, "dwz", GZW_);
        ////////////////////ここまで




        NSComputeShader.SetBuffer(kernelpressure0, "refDIV", DIV);
        NSComputeShader.SetBuffer(kernelpressure0, "YPN", YPN);
        NSComputeShader.SetBuffer(kernelpressure0, "WallP", WallP);

        NSComputeShader.SetBuffer(kernelpressure1, "refDIV", DIV);
        NSComputeShader.SetBuffer(kernelpressure1, "YPN", YPN);
        NSComputeShader.SetBuffer(kernelpressure1, "WallP", WallP);

        NSComputeShader.SetBuffer(kerneldiv, "DIV", DIV);
        NSComputeShader.SetBuffer(kerneldiv, "refYU", YUN);
        NSComputeShader.SetBuffer(kerneldiv, "refYV", YVN);
        NSComputeShader.SetBuffer(kerneldiv, "refYW", YWN);

        NSComputeShader.SetBuffer(kernelrhs, "YUN", YUN);
        NSComputeShader.SetBuffer(kernelrhs, "YVN", YVN);
        NSComputeShader.SetBuffer(kernelrhs, "YWN", YWN);
        NSComputeShader.SetBuffer(kernelrhs, "refYPN", YPN);
        NSComputeShader.SetBuffer(kernelrhs, "refWallX", WallX);
        NSComputeShader.SetBuffer(kernelrhs, "refWallY", WallY);
        NSComputeShader.SetBuffer(kernelrhs, "refWallZ", WallZ);


        NSComputeShader.SetBuffer(kernelVorticity, "YUN", YUN);
        NSComputeShader.SetBuffer(kernelVorticity, "YVN", YVN);
        NSComputeShader.SetBuffer(kernelVorticity, "VOR", VOR);

        NSComputeShader.SetBuffer(kernelparticle, "YUN", YUN);
        NSComputeShader.SetBuffer(kernelparticle, "YVN", YVN);
        NSComputeShader.SetBuffer(kernelparticle, "YWN", YWN);
        if (CO.isPARTICLE)
            NSComputeShader.SetBuffer(kernelparticle, "ParticlePos", ParticlePos);

        NSComputeShader.SetBuffer(kernelexforce0, "YUN", YUN);
        NSComputeShader.SetBuffer(kernelexforce0, "YVN", YVN);
        NSComputeShader.SetBuffer(kernelexforce0, "YWN", YWN);
        NSComputeShader.SetBuffer(kernelexforce0, "TPN", TPN);

        //texture rtセット
        NSComputeShader.SetTexture(kernelray0, "RTout", rt);
        NSComputeShader.SetBuffer(kernelray0, "WallP", WallP);
        NSComputeShader.SetBuffer(kernelray0, "TPN", TPN);
    }


    void SetWall()
    { 
        //壁からセット
        uint[] wallPini=new uint[CO.WXYZ];
        for (int i = 0; i < CO.WX; i++)
        {
            for (int j = 0; j < CO.WY; j++)
            {
                for (int k = 0; k < CO.WZ; k++) 
                {
                    wallPini[i + j * CO.WX + k * CO.WX * CO.WY] = 255;
                    if ((i == 0) | (k == 0) | (j == 0)) 
                    {
                        wallPini[i + j * CO.WX + k * CO.WX * CO.WY] = 0;
                    }
                }
                    
            }
        }


        uint[] wallxini = new uint[CO.WXYZ];
        uint[] wallyini = new uint[CO.WXYZ];
        uint[] wallzini = new uint[CO.WXYZ];
        //速度定義面を更新
        for (int i = 0; i < CO.WX; i++)
        {
            for (int j = 0; j < CO.WY; j++)
            {
                for (int k = 0; k < CO.WZ; k++)
                {
                    wallxini[i + j * CO.WX + k * CO.WX * CO.WY] = 255;
                    wallyini[i + j * CO.WX + k * CO.WX * CO.WY] = 255;
                    wallzini[i + j * CO.WX + k * CO.WX * CO.WY] = 255;
                    uint t0 = wallPini[i + j * CO.WX + k * CO.WX * CO.WY];
                    uint tx = wallPini[(i - 1 + CO.WX) % CO.WX + j * CO.WX + k * CO.WX * CO.WY];
                    uint ty = wallPini[i + (j - 1 + CO.WY) % CO.WY * CO.WX + k * CO.WX * CO.WY];
                    uint tz = wallPini[i + j * CO.WX + (k - 1 + CO.WZ) % CO.WZ * CO.WX * CO.WY];
                    
                    
                    if ((tx == 0)|(t0 == 0))
                    {
                        wallxini[i + j * CO.WX + k * CO.WX * CO.WY] = 0;
                    }

                    if ((ty == 0)| (t0 == 0))
                    {
                        wallyini[i + j * CO.WX + k * CO.WX * CO.WY] = 0;
                    }
                    if ((tz == 0) | (t0 == 0))
                    {
                        wallzini[i + j * CO.WX + k * CO.WX * CO.WY] = 0;
                    }
                    
                }

            }
        }


        WallP.SetData(wallPini);
        WallX.SetData(wallxini);
        WallY.SetData(wallyini);
        WallZ.SetData(wallzini);

    }







    void Veloc() 
    {
        NSComputeShader.Dispatch(kernelVeloc0, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);
        NSComputeShader.Dispatch(kernelVeloc1, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);
        return;
    }
    void CIP()
    {
        //偏微分成分もコピーする必要あり
        CopyBufferToBuffer_f(GXU_, GXU);
        CopyBufferToBuffer_f(GYU_, GYU);
        CopyBufferToBuffer_f(GZU_, GZU);

        CopyBufferToBuffer_f(GXV_, GXV);
        CopyBufferToBuffer_f(GYV_, GYV);
        CopyBufferToBuffer_f(GZV_, GZV);

        CopyBufferToBuffer_f(GXW_, GXW);
        CopyBufferToBuffer_f(GYW_, GYW);
        CopyBufferToBuffer_f(GZW_, GZW);

        //U速度移流について
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "u", YU);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "v", YVU);//Vの速度が必要
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "w", YWU);//Wの速度が必要
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "fn", YUN);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "gxn", GXU);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "gyn", GYU);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "gzn", GZU);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "GXd", GXU_);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "GYd", GYU_);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "GZd", GZU_);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "Yd", YU);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "Wall", WallX);
        NSComputeShader.Dispatch(kernelAdvectionCIP, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);

        //V速度移流について
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "u", YUV);//Uの速度が必要
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "v", YV);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "w", YWV);//Wの速度が必要
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "fn", YVN);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "gxn", GXV);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "gyn", GYV);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "gzn", GZV);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "GXd", GXV_);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "GYd", GYV_);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "GZd", GZV_);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "Yd", YV);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "Wall", WallY);
        NSComputeShader.Dispatch(kernelAdvectionCIP, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);

        //W速度移流について
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "u", YUW);//Uの速度が必要
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "v", YVW);//Vの速度が必要
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "w", YW);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "fn", YWN);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "gxn", GXW);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "gyn", GYW);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "gzn", GZW);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "GXd", GXW_);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "GYd", GYW_);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "GZd", GZW_);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "Yd", YW);
        NSComputeShader.SetBuffer(kernelAdvectionCIP, "Wall", WallZ);
        NSComputeShader.Dispatch(kernelAdvectionCIP, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);

        if (CO.isTEMPERATURE)
        {
            CopyBufferToBuffer_f(GXTP_, GXTP);
            CopyBufferToBuffer_f(GYTP_, GYTP);
            CopyBufferToBuffer_f(GZTP_, GZTP);
            //温度移流について
            NSComputeShader.SetBuffer(kernelAdvectionCIP, "u", YUT);
            NSComputeShader.SetBuffer(kernelAdvectionCIP, "v", YVT);
            NSComputeShader.SetBuffer(kernelAdvectionCIP, "w", YWT);
            NSComputeShader.SetBuffer(kernelAdvectionCIP, "fn", TPN);
            NSComputeShader.SetBuffer(kernelAdvectionCIP, "gxn", GXTP);
            NSComputeShader.SetBuffer(kernelAdvectionCIP, "gyn", GYTP);
            NSComputeShader.SetBuffer(kernelAdvectionCIP, "gzn", GZTP);
            NSComputeShader.SetBuffer(kernelAdvectionCIP, "GXd", GXTP_);
            NSComputeShader.SetBuffer(kernelAdvectionCIP, "GYd", GYTP_);
            NSComputeShader.SetBuffer(kernelAdvectionCIP, "GZd", GZTP_);
            NSComputeShader.SetBuffer(kernelAdvectionCIP, "Yd", TP);
            NSComputeShader.Dispatch(kernelAdvectionCIP, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);
        }
        return;
    }

    void Div()
    {
        NSComputeShader.Dispatch(kerneldiv, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);
    }
    void Pressure()
    {
        int loopnum = 8;
        if (cnt < 4) loopnum += 128;
            for (int i = 0; i < loopnum; i++) 
        {
            NSComputeShader.Dispatch(kernelpressure0, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);
            NSComputeShader.Dispatch(kernelpressure1, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);
        }
        return;
    }
    
    void Rhs()
    {
        NSComputeShader.Dispatch(kernelrhs, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);
        return;
    }
    void Newgrad()
    {
        //偏微分成分もコピーする必要あり
        CopyBufferToBuffer_f(GXU_, GXU);
        CopyBufferToBuffer_f(GYU_, GYU);
        CopyBufferToBuffer_f(GZU_, GZU);

        CopyBufferToBuffer_f(GXV_, GXV);
        CopyBufferToBuffer_f(GYV_, GYV);
        CopyBufferToBuffer_f(GZV_, GZV);

        CopyBufferToBuffer_f(GXW_, GXW);
        CopyBufferToBuffer_f(GYW_, GYW);
        CopyBufferToBuffer_f(GZW_, GZW);

        //U方向関連
        NSComputeShader.Dispatch(kernelNewgrad_x, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);

        //V方向関連
        NSComputeShader.Dispatch(kernelNewgrad_y, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);

        //W方向関連
        NSComputeShader.Dispatch(kernelNewgrad_z, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);

        if (CO.isTEMPERATURE)
        {
            //温度方向関連
            CopyBufferToBuffer_f(GXTP_, GXTP);
            CopyBufferToBuffer_f(GYTP_, GYTP);
            CopyBufferToBuffer_f(GZTP_, GZTP);
            NSComputeShader.Dispatch(kernelNewgrad_t, CO.WX / CO.blockX, CO.WY / CO.blockY, CO.WZ / CO.blockZ);
        }
        return;
    }



    void Particle_Move()
    {
        NSComputeShader.Dispatch(kernelparticle, CO.PARTICLENUM / 64, 1, 1);
    }


    void Exforce()
    {
        NSComputeShader.Dispatch(kernelexforce0, 1, 1, 1);
    }















    //全部0.0fで埋める関数 at CPU
    void FillBuffer_F(ComputeBuffer data, float fval = 0.0f)
    {
        float[] a = new float[data.count];
        for (int i = 0; i < data.count; i++)
        {
            a[i] = fval;
        }
        data.SetData(a);
    }

    //全部0(uint32)で埋める関数 at CPU
    void FillBuffer_UI(ComputeBuffer data,uint uival= 0 )
    {
        uint[] a = new uint[data.count];
        for (int i = 0; i < data.count; i++)
        {
            a[i] = uival;
        }
        data.SetData(a);
    }

    //vram同士のコピーuint限定
    //offset次第では書き込みオーバーフローも発生するため注意
    void CopyBufferToBuffer_ui(ComputeBuffer datadst, ComputeBuffer datasrc, int size = 0, int dstoffset = 0, int srcoffset = 0)//この場合のsizeはbyteではなく配列数、offsetもそう
    {
        if (size == 0)
        {
            size = datadst.count;
        }
        NSComputeShader.SetInt("SIZE", size);
        NSComputeShader.SetInt("OFFSETDST", dstoffset);
        NSComputeShader.SetInt("OFFSETSRC", srcoffset);
        NSComputeShader.SetBuffer(kernelcomputebuffermemcopy_i, "DATADSTI", datadst);
        NSComputeShader.SetBuffer(kernelcomputebuffermemcopy_i, "DATASRCI", datasrc);
        NSComputeShader.Dispatch(kernelcomputebuffermemcopy_i, (size + 63) / 64, 1, 1);
    }

    //vram同士のコピーfloat限定
    //offset次第では書き込みオーバーフローも発生するため注意
    void CopyBufferToBuffer_f(ComputeBuffer datadst, ComputeBuffer datasrc, int size = 0, int dstoffset = 0, int srcoffset = 0)//この場合のsizeはbyteではなく配列数、offsetもそう
    {
        if (size == 0)
        {
            size = datadst.count;
        }
        NSComputeShader.SetInt("SIZE", size);
        NSComputeShader.SetInt("OFFSETDST", dstoffset);
        NSComputeShader.SetInt("OFFSETSRC", srcoffset);
        NSComputeShader.SetBuffer(kernelcomputebuffermemcopy_f, "DATADSTF", datadst);
        NSComputeShader.SetBuffer(kernelcomputebuffermemcopy_f, "DATASRCF", datasrc);
        NSComputeShader.Dispatch(kernelcomputebuffermemcopy_f, (size + 63) / 64, 1, 1);
    }



    private RenderTexture CreateRT3(int width, int height, int depth)
    {
        var rd = new RenderTexture(width, height,0, RenderTextureFormat.RFloat);
        rd.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        rd.filterMode = FilterMode.Bilinear;
        rd.wrapMode = TextureWrapMode.Repeat;
        rd.volumeDepth = depth;
        //rd.colorFormat = RenderTextureFormat.RFloat;
        rd.enableRandomWrite = true;
        

        //var tex = new RenderTexture(rd);
        //Bilinear補間
        //tex.filterMode = FilterMode.Bilinear;
        //tex.wrapMode = TextureWrapMode.Repeat;
        rd.Create();
        return rd;
    }






    /// 破棄
    void OnDisable()
    {
        if (CO.isPARTICLE)
            ParticlePos.Release();

        YU.Release();
        YUN.Release();
        YV.Release();
        YVN.Release();
        YW.Release();
        YWN.Release();
        TP.Release();
        TPN.Release();

        YUT.Release();
        YVT.Release();
        YWT.Release();

        YUV.Release();
        YUW.Release();
        YWU.Release();
        YWV.Release();
        YVU.Release();
        YVW.Release();

        GXU.Release();
        GYU.Release();
        GZU.Release();
        GXV.Release();
        GYV.Release();
        GZV.Release();
        GXW.Release();
        GYW.Release();
        GZW.Release();
        GXTP.Release();
        GYTP.Release();
        GZTP.Release();
        GXU_.Release();
        GYU_.Release();
        GZU_.Release();
        GXV_.Release();
        GYV_.Release();
        GZV_.Release();
        GXW_.Release();
        GYW_.Release();
        GZW_.Release();
        GXTP_.Release();
        GYTP_.Release();
        GZTP_.Release();

        //座標関係ないスカラー系
        VOR.Release();
        YPN.Release();
        DIV.Release();

        WallX.Release();
        WallY.Release();
        WallZ.Release();
        WallP.Release();

    }
}