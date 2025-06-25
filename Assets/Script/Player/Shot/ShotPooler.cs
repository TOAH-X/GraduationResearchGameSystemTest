using System.Collections.Generic;
using UnityEngine;

public class ShotPooler : MonoBehaviour
{
    [Header("ショットのプレハブ")]
    [SerializeField] GameObject shotPrefab;
    [Header("プーリングサイズ")]
    [SerializeField] int poolSize = 100;
    [Header("ショットの親")]
    [SerializeField] GameObject shotParentObj;

    private Queue<GameObject> pool = new Queue<GameObject>();

    public static ShotPooler Instance { get; private set; }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;

        for (int i = 0; i < poolSize; i++)
        {
            GameObject shot = Instantiate(shotPrefab);
            shot.transform.SetParent(shotParentObj.transform);
            shot.SetActive(false);
            pool.Enqueue(shot);
        }
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        
    }

    public GameObject GetShot()
    {
        var shot = pool.Dequeue();
        shot.SetActive(true);
        return shot;
        /*
        if (pool.Count > 0)
        {
            var shot = pool.Dequeue();
            shot.SetActive(true);
            return shot;
        }
        else
        {
            // 追加で生成（必要なら）
            GameObject newShot = Instantiate(shotPrefab);
            newShot.transform.SetParent(shotParentObj.transform);
            return newShot;
        }
        */
    }

    public void ReturnShot(GameObject shot)
    {
        shot.SetActive(false);
        pool.Enqueue(shot);
    }
}
