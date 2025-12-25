using System.Collections.Generic;
using UnityEngine;

public class BulletPooler : MonoBehaviour
{
    [Header("弾幕のプレハブ")]
    [SerializeField] GameObject bulletPrefab;
    [Header("プーリングサイズ")]
    [SerializeField] int poolSize = 100;
    [Header("バレットの親")]
    [SerializeField] GameObject bulletParentObj;

    private Queue<GameObject> pool = new Queue<GameObject>();

    public static BulletPooler Instance { get; private set; }

    // Start is called before the first frame update
    void Start()
    {

    }

    private void Awake()
    {
        if (Instance == null) Instance = this;

        for (int i = 0; i < poolSize; i++)
        {
            GameObject bullet = Instantiate(bulletPrefab);
            bullet.transform.SetParent(bulletParentObj.transform);
            bullet.SetActive(false);
            pool.Enqueue(bullet);
        }
    }

    // Update is called once per frame
    void Update()
    {
        //Debug.Log(poolSize - pool.Count);
    }

    public GameObject GetBullet()
    {
        if (pool.Count > 0)
        {
            var bullet = pool.Dequeue();
            bullet.SetActive(true);
            return bullet;
        }
        else
        {
            return null;
        }
    }

    public void ReturnBullet (GameObject bullet)
    {
        bullet.SetActive(false);
        pool.Enqueue(bullet);
    }
}
