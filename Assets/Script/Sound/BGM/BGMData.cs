using UnityEngine;
using System;

[Serializable]
public class BGMData
{
    public BGMType type;
    public AudioClip clip;
    [Range(0f, 1f)]
    public float volume = 1f;
}