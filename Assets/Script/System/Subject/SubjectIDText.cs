using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class SubjectIDText : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI subjectIDText;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        subjectIDText.text = "被験者ID:" + SubjectManager.Instance.SubjectID;
    }
}
