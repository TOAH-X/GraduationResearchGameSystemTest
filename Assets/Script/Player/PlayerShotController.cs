using UnityEngine;

public class PlayerShotController : MonoBehaviour
{
    [Header("ショットプレハブ")]
    [SerializeField] GameObject shotPrefab;
    [Header("PlayerID")]
    [SerializeField] int playerID;
    [Header("PlayerAgentスクリプト")]
    [SerializeField] RewardManager rewardManager;
    [Header("PlayerBaseスクリプト")]
    [SerializeField] PlayerBase playerBaseScript;

    [Header("ショット格納先(親)")]
    [SerializeField] GameObject shotParentObj;

    float timer = 0;

    bool isShotTrigger = false;

    // Start is called before the first frame update
    void Start()
    {
        playerBaseScript = this.GetComponent<PlayerBase>();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        // 念の為参照(Startでも良いかも)
        playerID = playerBaseScript.PlayerID;

        if (timer >= 0.1f)
        {
            if (Input.GetKey(KeyCode.Z) || isShotTrigger == true) 
            {
                bool isSlow = false;

                // 非低速モード(拡散ショット)
                if (Input.GetKey(KeyCode.LeftShift) == false)
                {
                    isSlow = false;
                }
                // 低速モード(集中ショット)
                else
                {
                    isSlow = true;
                }
                ShotGroup(isSlow);

                timer = 0;
            }
            isShotTrigger = false;
        }
        timer += Time.fixedDeltaTime;
        

    }

    // ショットトリガー(agent用)
    public void ShotTrigger(bool isShot) 
    {
        isShotTrigger = isShot;
    }

    // ショット
    public void ShotGroup(bool isSlow) 
    {
        if (playerBaseScript.IsRestart == false) 
        {
            //中央ショット
            GenerateShot(new Vector3(-0.25f, 0), 20f, 0f);
            GenerateShot(new Vector3(0.25f, 0), 20f, 0f);
            GenerateShot(new Vector3(-0.125f, 0), 20f, 0f);
            GenerateShot(new Vector3(0.125f, 0), 20f, 0f);

            GenerateShot(new Vector3(-0.375f, 0), 20f, 0f);
            GenerateShot(new Vector3(0.375f, 0), 20f, 0f);

            // 非低速モード
            if (isSlow == false)
            {
                /*
                GenerateShot(new Vector3(-0.5f, 0.5f), 20f, 30f);
                GenerateShot(new Vector3(0.5f, 0.5f), 20f, -30f);
                GenerateShot(new Vector3(-0.5f, 0.5f), 20f, 15f);
                GenerateShot(new Vector3(0.5f, 0.5f), 20f, -15f);
                */
            }
            //低速モード
            else
            {
                /*
                GenerateShot(new Vector3(-0.5f, 0.5f), 20f, 10f);
                GenerateShot(new Vector3(0.5f, 0.5f), 20f, -10f);
                GenerateShot(new Vector3(-0.5f, 0.5f), 20f, 5f);
                GenerateShot(new Vector3(0.5f, 0.5f), 20f, -5f);
                */
            }
        }
    }

    /// <summary>
    /// ショットの生成
    /// </summary>
    /// <param name="pos">生成位置</param>
    /// <param name="moveSpeed">発射測度</param>
    /// <param name="angleDegree">発射角度</param>
    public void GenerateShot(Vector3 pos, float moveSpeed, float angleDegree)
    {
        // ショット生成
        //Vector2 pos = transform.position + new Vector3(0.25f, 0);
        if (shotParentObj == null)
        {
            shotParentObj = GameObject.Find("ShotParent");      //重いので余裕があればインスタンス化
        }
        //var shotPrefabs = Instantiate(shotPrefab, transform.position + pos, Quaternion.identity, shotParentObj.transform);
        //shotPrefabs.transform.localEulerAngles = new Vector3(0, 0, angleDegree);                    //生成場所で書き換えましょう。無駄な行です。
        //ShotController shotControllerScript = shotPrefabs.GetComponent<ShotController>();
        //shotControllerScript.MoveSpeed = moveSpeed;
        //shotControllerScript.AngleDegree = angleDegree;
        //shotControllerScript.PlayerID = playerID;

        // プールから取得して設定
        var shotObj = ShotPooler.Instance.GetShot();
        shotObj.transform.SetParent(shotParentObj.transform);
        shotObj.transform.position = transform.position + pos;
        shotObj.transform.rotation = Quaternion.Euler(0, 0, angleDegree);

        ShotController shotControllerScript = shotObj.GetComponent<ShotController>();
        shotControllerScript.MoveSpeed = moveSpeed;
        shotControllerScript.AngleDegree = angleDegree;
        shotControllerScript.PlayerID = playerID;
        shotControllerScript.Initialize(rewardManager);
    }
}
