using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Const;

public class CameraDir : MonoBehaviour
{
    float c0length = 194.0f;//カメラの注視点までの距離
    void Start()
    {
        
    }

    void Update()
    {
        //左右キー操作でカメラの座標を変える
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            transform.Rotate(0, -6, 0);
        }
        if (Input.GetKey(KeyCode.RightArrow))
        {
            transform.Rotate(0, 6, 0);
        }
        //上下キー操作でカメラの座標を変える
        if (Input.GetKey(KeyCode.UpArrow))
        {
            transform.Rotate(-6, 0, 0);
        }
        if (Input.GetKey(KeyCode.DownArrow))
        {
            transform.Rotate(6, 0, 0);
        }
        //マウスホイールでc0lengthを変える
        c0length -= Input.GetAxis("Mouse ScrollWheel") * (41.97f);

        //カメラは向いている方角に応じてシミュレーションboxの中心から一定距離離れた座標に存在する
        transform.position = new Vector3(0.5f * CO.WX, 0.5f * CO.WY, 0.5f * CO.WZ) - transform.forward * c0length;
    }

    //カメラの現在の座標をベクトル3で返す
    public Vector3 GetCameraPos()
    {
        return transform.position;
    }
    //カメラの現在の向いている方向をベクトル3で返す
    public Vector3 GetCameraDir()
    {
        return transform.forward;
    }
    //カメラの右方向のベクトルを返す
    public Vector3 GetCameraRight()
    {
        return transform.right;
    }
    //カメラの上方向のベクトルを返す
    public Vector3 GetCameraUp()
    {
        return transform.up;
    }

}
