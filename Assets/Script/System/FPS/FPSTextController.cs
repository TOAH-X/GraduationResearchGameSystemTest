using UnityEngine;
using TMPro;

public class FPSTextController : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI fpsText;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        fpsText.text = FPSController.GetFPS().ToString("F1") + "fps";
    }
}
