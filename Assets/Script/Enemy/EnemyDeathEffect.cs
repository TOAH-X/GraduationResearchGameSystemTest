using UnityEngine;

public class EnemyDeathEffect: MonoBehaviour
{
    public static EnemyDeathEffect Instance { get; private set; }

    [Header("死亡エフェクト")]
    [SerializeField] ParticleSystem enemyDeathEffectParticle;
    [SerializeField] private Transform particleParent;

    void Awake()
    {
        // シングルトン
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // 死亡エフェクトの生成
    public void PlayEffect(Vector2 position) 
    {
        // 生成
        Quaternion rotation = Quaternion.Euler(0f, 0f, 0f);
        ParticleSystem enemyDeathEffectParticleInstance = Instantiate(enemyDeathEffectParticle, position, rotation, particleParent);
        // 再生
        enemyDeathEffectParticleInstance.Play();
        // 再生終了後消去
        Destroy(enemyDeathEffectParticleInstance.gameObject, enemyDeathEffectParticleInstance.main.duration + enemyDeathEffectParticleInstance.main.startLifetime.constantMax);
    }
}
