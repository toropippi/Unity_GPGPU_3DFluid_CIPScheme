using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Capture : MonoBehaviour
{
    int cnt = 0;
    // Start is called before the first frame update
    void Start()
    {
        cnt = 0;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Capturef()
    {
        string title = "images/image";
        string name = title + cnt.ToString() + ".png";
        ScreenCapture.CaptureScreenshot(name);
        cnt++;
    }
}
