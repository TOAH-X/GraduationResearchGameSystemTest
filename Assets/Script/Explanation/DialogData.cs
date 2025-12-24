using UnityEngine;

[System.Serializable]
public struct DialogLine
{
    public string name;           // 名前
    [TextArea(3, 10)]
    public string message;        // 内容
    public Sprite characterImage; // 立ち絵（表情差分はここでSpriteを差し替える）
}

[CreateAssetMenu(fileName = "NewDialog", menuName = "DialogData")]
public class DialogData : ScriptableObject
{
    public DialogLine[] lines;
}