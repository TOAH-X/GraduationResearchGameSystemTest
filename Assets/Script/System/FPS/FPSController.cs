using UnityEngine;

public class FPSController
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]

    public static void FPS()
    {
        Application.targetFrameRate = 60;
    }

    public static float GetFPS() 
    {
        float fps = 1f / Time.fixedDeltaTime;
        return fps;
    }
}
