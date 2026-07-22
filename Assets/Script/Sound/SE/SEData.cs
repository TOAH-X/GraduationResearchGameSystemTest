using System;
using UnityEngine;

[Serializable]
public class SEData
{
    public SEType type;
    public AudioClip clip;
    [Range(0f, 1f)]
    public float volume = 1f;
}
