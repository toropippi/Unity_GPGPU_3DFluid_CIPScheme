using UnityEngine;
using System.Runtime.InteropServices;


/// <summary>
/// 沢山の弾を管理するクラス
/// </summary>
public class FluidSimCIP3D : MonoBehaviour
{
    public const int WX = 32;
    public const int WY = 32;
    public const int WZ = 32;
    public const int PARTICLENUM = 65536 * 16;
    const int blockX = 32;
    const int blockY = 1;
    const int blockZ = 1;

    const int WXYZ = WX * WY * WZ;

    //下記のは直接shaderに書き込み
    //const float DT = 1.00f;//デルタタイム
    //const float MU = 0.0000000001f;//粘性項μ。大きいほどぬめっとしてる
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
    int kernelpressure0;
    int kernelpressure1;
    int kerneldiv;
    int kernelrhs;
    int kernelparticle;
    int kernelexforce0;

    ComputeBuffer YU;//速度値
    ComputeBuffer YUN;//速度値
    ComputeBuffer YV;//速度値
    ComputeBuffer YVN;//速度値
    ComputeBuffer YW;//速度値
    ComputeBuffer YWN;//速度値

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
    ComputeBuffer GXU_;//u速度x偏微分
    ComputeBuffer GYU_;//u速度y偏微分
    ComputeBuffer GZU_;//u速度z偏微分
    ComputeBuffer GXV_;//v速度x偏微分
    ComputeBuffer GYV_;//v速度y偏微分
    ComputeBuffer GZV_;//v速度z偏微分
    ComputeBuffer GXW_;//w速度x偏微分
    ComputeBuffer GYW_;//w速度y偏微分
    ComputeBuffer GZW_;//w速度z偏微分

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

    int cnt = 0;
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
        //NSComputeShader.SetFloat("DeltaTime", Time.deltaTime);
        //NSComputeShader.Dispatch(kernelCSMain, ParticlePos.count / 256, 1, 1);


        //この時点でYUN,YVN,YWNだけが最新
        for (int loopf = 0; loopf < 2; loopf++)
        {
            //移流フェーズ
            CopyBufferToBuffer_f(YU, YUN);
            CopyBufferToBuffer_f(YV, YVN);
            CopyBufferToBuffer_f(YW, YWN);
            Veloc();
            CIP();
            CopyBufferToBuffer_f(YU, YUN);
            CopyBufferToBuffer_f(YV, YVN);
            CopyBufferToBuffer_f(YW, YWN);

            //非移流フェーズ
            if (cnt < 9992)
                Exforce();
            Div();
            Pressure();
            Rhs();
            Newgrad();

            Particle_Move();
            cnt++;

        }


        //最後にガス抜き。毎フレームやらなくてもいいかも
        GasReleasing();

        
        //キャプチャ
        /*
        if (cnt%1==0)
            GetComponent<Capture>().Capturef();
        */
    }


    //圧力の平均値がどんどん高くなっている場合に平均が0になるような処理を
    void GasReleasing()
    {
        //面倒なのでCPUで全部やってる
        float[] cpudiv = new float[WX * WY * WZ];
        YPN.GetData(cpudiv);
        float gk = 0.0f;
        for (int i = 0; i < WXYZ; i++)
        {
            gk += cpudiv[i];
        }
        //Debug.Log(gk);
        gk /= WXYZ;

        for (int i = 0; i < WXYZ; i++)
        {
            cpudiv[i] -= gk;
        }
        YPN.SetData(cpudiv);


        //念のためdivergenceを計算して表示
        if ((cnt % 120) == 0)
        {
            Div();
            cpudiv = new float[WX * WY * WZ];
            DIV.GetData(cpudiv);
            gk = 0.0f;
            for (int i = 0; i < WXYZ; i++)
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
        kernelpressure0 = NSComputeShader.FindKernel("pressure0");
        kernelpressure1 = NSComputeShader.FindKernel("pressure1");
        kerneldiv = NSComputeShader.FindKernel("div");
        kernelrhs = NSComputeShader.FindKernel("rhs");
        kernelVorticity = NSComputeShader.FindKernel("Vorticity");
        kernelparticle = NSComputeShader.FindKernel("particle_move");
        kernelexforce0 = NSComputeShader.FindKernel("exforce0");

        kernelcomputebuffermemcopy_i = NSComputeShader.FindKernel("ComputeBufferMemcopy_i");
        kernelcomputebuffermemcopy_f = NSComputeShader.FindKernel("ComputeBufferMemcopy_f");
    }

    /// コンピュートバッファの初期化
    void InitializeComputeBuffer()
    {
        YU = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        YUN = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        YV = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        YVN = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        YW = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        YWN = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));

        YUT = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        YVT = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        YWT = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));

        YUV = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        YUW = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        YWU = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        YWV = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        YVU = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        YVW = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));

        GXU = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GYU = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GZU = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GXV = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GYV = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GZV = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GXW = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GYW = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GZW = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GXU_ = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GYU_ = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GZU_ = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GXV_ = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GYV_ = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GZV_ = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GXW_ = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GYW_ = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        GZW_ = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));

        //座標関係ないスカラー系
        VOR = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        YPN = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));
        DIV = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(float)));

        WallX = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(uint)));
        WallY = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(uint)));
        WallZ = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(uint)));
        WallP = new ComputeBuffer(WXYZ, Marshal.SizeOf(typeof(uint)));

        //その他
        // 弾数は1万個
        ParticlePos = new ComputeBuffer(PARTICLENUM, Marshal.SizeOf(typeof(Vector3)));

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
        /*
        
	YUN[10 + 16 * WX + 16 * WX * WY] = 0.29;
	YUN[10 + 16 * WX + 15 * WX * WY] = 0.29;
	YUN[10 + 15 * WX + 16 * WX * WY] = 0.29;
	YUN[10 + 15 * WX + 15 * WX * WY] = 0.29;
    */

        // 代入
        ParticlePos.SetData(bullets);

        FillComputeBuffer();
    }

    void FillComputeBuffer()
    {
        FillBuffer_F(YUN);
        FillBuffer_F(YVN);
        FillBuffer_F(YWN);
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
        NSComputeShader.SetBuffer(kernelparticle, "ParticlePos", ParticlePos);

        NSComputeShader.SetBuffer(kernelexforce0, "YUN", YUN);
        NSComputeShader.SetBuffer(kernelexforce0, "YVN", YVN);
        NSComputeShader.SetBuffer(kernelexforce0, "YWN", YWN);

        // テクスチャ、マテリアル関連
        //bulletsMaterial.SetBuffer("ParticlePos", ParticlePos);
    }


    void SetWall()
    { 
        //壁からセット
        uint[] wallPini=new uint[WXYZ];
        for (int i = 0; i < WX; i++)
        {
            for (int j = 0; j < WY; j++)
            {
                for (int k = 0; k < WZ; k++) 
                {
                    wallPini[i + j * WX + k * WX * WY] = 255;
                    if ((i == 0) | (k == 0) | (j == 0)) 
                    {
                        wallPini[i + j * WX + k * WX * WY] = 0;
                    }
                }
                    
            }
        }


        uint[] wallxini = new uint[WXYZ];
        uint[] wallyini = new uint[WXYZ];
        uint[] wallzini = new uint[WXYZ];
        //速度定義面を更新
        for (int i = 0; i < WX; i++)
        {
            for (int j = 0; j < WY; j++)
            {
                for (int k = 0; k < WZ; k++)
                {
                    wallxini[i + j * WX + k * WX * WY] = 255;
                    wallyini[i + j * WX + k * WX * WY] = 255;
                    wallzini[i + j * WX + k * WX * WY] = 255;
                    uint t0 = wallPini[i + j * WX + k * WX * WY];
                    uint tx = wallPini[(i - 1 + WX) % WX + j * WX + k * WX * WY];
                    uint ty = wallPini[i + (j - 1 + WY) % WY * WX + k * WX * WY];
                    uint tz = wallPini[i + j * WX + (k - 1 + WZ) % WZ * WX * WY];
                    
                    
                    if ((tx == 0)|(t0 == 0))
                    {
                        wallxini[i + j * WX + k * WX * WY] = 0;
                    }

                    if ((ty == 0)| (t0 == 0))
                    {
                        wallyini[i + j * WX + k * WX * WY] = 0;
                    }
                    if ((tz == 0) | (t0 == 0))
                    {
                        wallzini[i + j * WX + k * WX * WY] = 0;
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
        NSComputeShader.Dispatch(kernelVeloc0, WX / blockX, WY / blockY, WZ / blockZ);
        NSComputeShader.Dispatch(kernelVeloc1, WX / blockX, WY / blockY, WZ / blockZ);
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
        NSComputeShader.Dispatch(kernelAdvectionCIP, WX / blockX, WY / blockY, WZ / blockZ);

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
        NSComputeShader.Dispatch(kernelAdvectionCIP, WX / blockX, WY / blockY, WZ / blockZ);

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
        NSComputeShader.Dispatch(kernelAdvectionCIP, WX / blockX, WY / blockY, WZ / blockZ);
        return;
    }

    void Div()
    {
        NSComputeShader.Dispatch(kerneldiv, WX / blockX, WY / blockY, WZ / blockZ);
    }
    void Pressure()
    {
        int loopnum = 64;
        if (cnt < 150) loopnum += 2048;
            for (int i = 0; i < loopnum; i++) 
        {
            NSComputeShader.Dispatch(kernelpressure0, WX / blockX, WY / blockY, WZ / blockZ);
            NSComputeShader.Dispatch(kernelpressure1, WX / blockX, WY / blockY, WZ / blockZ);
        }
        return;
    }
    
    void Rhs()
    {
        NSComputeShader.Dispatch(kernelrhs, WX / blockX, WY / blockY, WZ / blockZ);
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
        NSComputeShader.Dispatch(kernelNewgrad_x, WX / blockX, WY / blockY, WZ / blockZ);

        //V方向関連
        NSComputeShader.Dispatch(kernelNewgrad_y, WX / blockX, WY / blockY, WZ / blockZ);

        //W方向関連
        NSComputeShader.Dispatch(kernelNewgrad_z, WX / blockX, WY / blockY, WZ / blockZ);
        return;
    }



    void Particle_Move()
    {
        NSComputeShader.Dispatch(kernelparticle, PARTICLENUM / 64, 1, 1);
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




















    /// <summary>
    /// レンダリング
    /// </summary>
    void OnRenderObject()
    {
        // レンダリングを開始
        //bulletsMaterial.SetPass(0);
        // 1万個のオブジェクトをレンダリング
        //Graphics.DrawProceduralNow(MeshTopology.Points, ParticlePos.count);
    }

    /// <summary>
    /// 破棄
    /// </summary>
    void OnDisable()
    {
        // コンピュートバッファは明示的に破棄しないと怒られます
        ParticlePos.Release();

        YU.Release();
        YUN.Release();
        YV.Release();
        YVN.Release();
        YW.Release();
        YWN.Release();

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
        GXU_.Release();
        GYU_.Release();
        GZU_.Release();
        GXV_.Release();
        GYV_.Release();
        GZV_.Release();
        GXW_.Release();
        GYW_.Release();
        GZW_.Release();

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