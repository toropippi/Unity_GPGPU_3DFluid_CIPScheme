using UnityEngine;

namespace Const
{
    public static class CO
    {
        //CFD計算関連
        public const int WX = 128;
        public const int WY = 128;
        public const int WZ = 128;

        public const int PARTICLENUM = 65536 * 16;
        public const int blockX = 32;
        public const int blockY = 1;
        public const int blockZ = 1;
        public const int WXYZ = WX * WY * WZ;

        //パーティクル計算をするか
        public const bool isPARTICLE = false;
        //温度移流計算するか
        public const bool isTEMPERATURE = true;

    }

    public static class FUNC
    {
        public static (Texture2D, Sprite) Create6464Tex()
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            //Texture2DからSpriteを作成
            var sprite = Sprite.Create(
              texture: tex,
              rect: new Rect(0, 0, 64, 64),
              pivot: new Vector2(0.5f, 0.5f)
            );
            for (int i = 0; i < 64; i++)
            {
                for (int j = 0; j < 64; j++)
                {
                    tex.SetPixel(i, j, new Color(1.0f, 1.0f, 1.0f, 1.0f));
                }
            }
            tex.Apply();

            return (tex, sprite);
        }



        //ここからはbytes読み込みのやつ
        //中身は.bmpである必要がある
        //pathの階層は/スラッシュ区切りで
        public static byte[] ReadBytesFile(string path)
        {
            var text_asset = Resources.Load(path) as TextAsset;
            byte[] raw_data = text_asset.bytes;
            return raw_data;
        }

        //bmpを読み込んで2次元配列を出力する関数
        public static int[,] LoadBmp(string path)
        {
            byte[] readBinary = ReadBytesFile(path);
            int pos = 18; // 18バイトから開始
            int width = 0;
            for (int i = 0; i < 3; i++)
            {
                width = width + readBinary[pos + i] * (1 << (8 * i));
            }
            int height = 0;
            pos += 4;
            for (int i = 0; i < 3; i++)
            {
                height = height + readBinary[pos + i] * (1 << (8 * i));
            }
            pos = 54;
            int[,] data = new int[width, height];

            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    data[i, height - j - 1] = readBinary[pos++] * 65536;
                    data[i, height - j - 1] += readBinary[pos++] * 256;
                    data[i, height - j - 1] += readBinary[pos++];
                }
            }
            return data;
        }


        //そのまま
        public static int BoolToInt(bool b0)
        {
            if (b0 == true)
            {
                return 1;
            }
            else
            {
                return 0;
            }
        }


    }

}
