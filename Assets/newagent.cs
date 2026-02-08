using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

public class Parasaurolophus : Agent
{
    [Header("Target Settings")]
    public Transform target;
    public Transform field;

    [System.Serializable]
    public struct JointConfig
    {
        public string jointName;           
        public ArticulationBody body;      
        [Range(0.001f, 1.0f)]
        public float energyCost; 
    }

    [Header("Body Settings")]
    public ArticulationBody rootArticulationBody;
    public JointConfig[] bodyParts; 

    [Header("Spawn Settings")]
    public Vector3 fixedSpawnPosition = new Vector3(0f, 3.4f, 0f);
    public Vector3 baseRotationEuler = new Vector3(-280f, 0f, 90f);

    [Header("Target Spawn Settings")]
    public float minTargetDistance = 10.0f;
    public float maxTargetDistance = 20.0f;

    [Header("Training Hyperparameters")]
    public float smoothnessLambda = 0.5f;
    public float targetWalkingSpeed = 2.0f; 

    [Header("Head Stability Settings")]
    public Transform headTransform;
    public float targetHeadHeight = 2.0f; 
    public float bobbingPenaltyWeight = 0.1f; 

    [Header("Debug (学習時はオフにすること!)")]
    public bool debugMode = false; 
    [Range(0, 4)] 
    public float debugLessonValue = 0.0f;

    // 転倒統計ロガー
    public FallStatsLogger statsLogger;

    // --- カリキュラム変数 ---
    private float currentLesson = 0.0f; 
    private float mobilityFactor = 0.0f;    
    private float headStabWeight = 0.0f;    
    private bool isRandomSpawn = false;     
    private bool isSpeedLimitActive = false; 
    private float lastLesson = -1.0f;

    // 内部変数
    private float initialTargetDistance;
    private bool hasFallen = false;
    private float[] previousActions; 
    private float prevHeadY;

    // 姿勢復元用データ
    private float[] defaultXTargets;
    private float[] defaultYTargets;
    private float[] defaultZTargets;
    private List<float> initialJointPositions = new List<float>();

    // 足ボーナス用の参照キャッシュ
    private ArticulationBody leftThighBody;
    private ArticulationBody rightThighBody;
    private Transform leftFootTransform;
    private Transform rightFootTransform;

    public override void Initialize()
    {
        base.Initialize();

        // ロガー自動取得
        if (statsLogger == null)
        {
            statsLogger = FindFirstObjectByType<FallStatsLogger>();
        }
        
        // Root自動取得
        if (rootArticulationBody == null)
        {
            foreach (var part in bodyParts)
            {
                if (part.body != null && part.body.isRoot)
                {
                    rootArticulationBody = part.body;
                    break;
                }
            }
            if (rootArticulationBody == null) rootArticulationBody = GetComponent<ArticulationBody>();
        }

        previousActions = new float[bodyParts.Length];

        // 足ボーナス用のパーツ特定
        foreach (var part in bodyParts)
        {
            if (part.body == null) continue;
            string name = part.jointName.ToLower();

            // 太もも (Thigh)
            if (name.Contains("thigh"))
            {
                if (name.Contains("_l") || name.Contains("left")) leftThighBody = part.body;
                else if (name.Contains("_r") || name.Contains("right")) rightThighBody = part.body;
            }
            // 足先 (Foot)
            if (name.Contains("foot"))
            {
                if (name.Contains("_l") || name.Contains("left")) leftFootTransform = part.body.transform;
                else if (name.Contains("_r") || name.Contains("right")) rightFootTransform = part.body.transform;
            }
        }

        // 初期姿勢（角度）の保存
        SetupDefaultTargets(); 

        // 物理リセット用の生データ保存
        if (rootArticulationBody != null)
        {
            rootArticulationBody.GetJointPositions(initialJointPositions);
        }

        if (headTransform != null) prevHeadY = headTransform.position.y;
    }

    void SetupDefaultTargets()
    {
        int count = bodyParts.Length;
        defaultXTargets = new float[count];
        defaultYTargets = new float[count];
        defaultZTargets = new float[count];

        for (int i = 0; i < count; i++)
        {
            if (bodyParts[i].body == null) continue;
            var body = bodyParts[i].body;
            if (body.dofCount > 0)
            {
                if (body.jointType == ArticulationJointType.SphericalJoint)
                {
                    int idx = 0;
                    if (body.twistLock != ArticulationDofLock.LockedMotion) defaultXTargets[i] = body.jointPosition[idx++] * Mathf.Rad2Deg;
                    if (body.swingYLock != ArticulationDofLock.LockedMotion) defaultYTargets[i] = body.jointPosition[idx++] * Mathf.Rad2Deg;
                    if (body.swingZLock != ArticulationDofLock.LockedMotion) defaultZTargets[i] = body.jointPosition[idx++] * Mathf.Rad2Deg;
                }
                else if (body.jointType == ArticulationJointType.RevoluteJoint)
                {
                    defaultXTargets[i] = body.jointPosition[0] * Mathf.Rad2Deg;
                }
            }
        }
    }

    void UpdateCurriculumParam()
    {
        if (debugMode)
        {
            currentLesson = debugLessonValue;
        }
        else
        {
            currentLesson = Academy.Instance.EnvironmentParameters.GetWithDefault("curriculum_level", 0.0f);
        }

        // レッスン変更ログ
        if (Mathf.Abs(currentLesson - lastLesson) > 0.1f)
        {
            string lessonName = "";
            if (currentLesson < 0.5f) lessonName = "Lesson 0";
            else if (currentLesson < 1.5f) lessonName = "Lesson 1";
            else if (currentLesson < 2.5f) lessonName = "Lesson 2";
            else if (currentLesson < 3.5f) lessonName = "Lesson 3";
            else lessonName = "Lesson 4";

            Debug.Log($"<color=cyan><b>【カリキュラム更新】 {lessonName} (Level: {currentLesson}) に移行</b></color>");
            lastLesson = currentLesson;
        }

        // パラメータ設定
        if (currentLesson < 0.5f) // Lesson 0
        {
            mobilityFactor = 0.0f; headStabWeight = 0.0f; isRandomSpawn = false; isSpeedLimitActive = false;
        }
        else if (currentLesson < 1.5f) // Lesson 1
        {
            mobilityFactor = 0.0f; headStabWeight = 5.0f; isRandomSpawn = false; isSpeedLimitActive = false;
        }
        else if (currentLesson < 2.5f) // Lesson 2
        {
            mobilityFactor = 0.5f; headStabWeight = 1.0f; isRandomSpawn = true; isSpeedLimitActive = false;
        }
        else if (currentLesson < 3.5f) // Lesson 3
        {
            mobilityFactor = 0.5f; headStabWeight = 1.0f; isRandomSpawn = true; isSpeedLimitActive = true;
        }
        else // Lesson 4
        {
            mobilityFactor = 1.0f; headStabWeight = 1.0f; isRandomSpawn = true; isSpeedLimitActive = true;
        }
    }

    public override void OnEpisodeBegin()
    {
        UpdateCurriculumParam(); // 呼び出し

        float randomYAngle;
        if (isRandomSpawn) randomYAngle = Random.Range(-180f, 180f);
        else randomYAngle = Random.Range(-5f, 5f);
        
        Quaternion baseRot = Quaternion.Euler(baseRotationEuler);
        Quaternion randomHeading = Quaternion.Euler(0f, randomYAngle, 0f);
        Quaternion resetRotation = randomHeading * baseRot;

        if (rootArticulationBody != null)
        {
            // 位置のリセット
            Vector3 worldSpawnPos = (field != null) ? field.position + fixedSpawnPosition : fixedSpawnPosition;
            rootArticulationBody.TeleportRoot(worldSpawnPos, resetRotation);
            
            // 物理リセット
            rootArticulationBody.SetJointPositions(initialJointPositions);
            rootArticulationBody.Sleep();
        }

        // ドライブ設定のリセット
        float rigidStiffness = 20000f; 
        float baseStiffness = 3000f; 

        for (int i = 0; i < bodyParts.Length; i++)
        {
            var part = bodyParts[i];
            if (part.body == null) continue;

            part.body.linearVelocity = Vector3.zero;
            part.body.angularVelocity = Vector3.zero;

            float currentStiffness = Mathf.Lerp(rigidStiffness, baseStiffness, mobilityFactor);

            var xDrive = part.body.xDrive; xDrive.target = defaultXTargets[i]; part.body.xDrive = xDrive;

            if (part.body.dofCount > 1)
            {
                var yDrive = part.body.yDrive; yDrive.target = defaultYTargets[i];
                yDrive.stiffness = currentStiffness; 
                part.body.yDrive = yDrive;

                var zDrive = part.body.zDrive; zDrive.target = defaultZTargets[i];
                zDrive.stiffness = currentStiffness;
                part.body.zDrive = zDrive;
            }
        }

        if (target != null) SpawnTarget(randomYAngle);

        System.Array.Clear(previousActions, 0, previousActions.Length);
        if (target != null) initialTargetDistance = Vector3.Distance(transform.position, target.position);
        hasFallen = false;
        if (headTransform != null) prevHeadY = headTransform.position.y;
    }

    void SpawnTarget(float agentHeadingAngle)
    {
        if (field == null) return; 

        float distance = Random.Range(minTargetDistance, maxTargetDistance);
        float spawnAngle;

        if (!isRandomSpawn) // Lesson 0, 1
        {
            spawnAngle = agentHeadingAngle; 
        }
        else // Lesson 2+
        {
            spawnAngle = Random.Range(0f, 360f);
        }

        Quaternion rot = Quaternion.Euler(0f, spawnAngle, 0f);
        Vector3 dir = rot * Vector3.forward;
        
        Vector3 centerPos = field.position + fixedSpawnPosition;
        Vector3 newPos = centerPos + (dir * distance);
        newPos.y = field.position.y + 1.5f; 
        
        target.position = newPos;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (float.IsNaN(transform.localPosition.x)) return;

        sensor.AddObservation(transform.localPosition);
        sensor.AddObservation(transform.localRotation);

        if (target != null) sensor.AddObservation(target.localPosition - transform.localPosition);
        else sensor.AddObservation(Vector3.zero);

        foreach (var part in bodyParts)
        {
            if (part.body != null && part.body.dofCount > 0)
            {
                float angle = part.body.jointPosition[0]; 
                float vel = part.body.jointVelocity[0];
                sensor.AddObservation(angle);
                sensor.AddObservation(vel);
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }
        }

        if (headTransform != null) sensor.AddObservation(headTransform.position.y);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var act = actions.ContinuousActions;
        int i = 0;
        float totalEnergyCost = 0f;

        foreach (var part in bodyParts)
        {
            if (part.body == null) { i++; continue; }

            float currentAction = Mathf.Clamp(act[i], -1f, 1f);
            
            // Lesson 0 ならエネルギーコスト計算を無視
            if (currentLesson > 0.5f) 
            {
                float prevAction = previousActions[i];
                float magnitudeTerm = currentAction * currentAction;
                float diff = currentAction - prevAction;
                float smoothnessTerm = diff * diff;
                float jointCost = part.energyCost * (magnitudeTerm + (smoothnessLambda * smoothnessTerm));
                totalEnergyCost += jointCost;
            }

            float defaultTarget = defaultXTargets[i];
            var drive = part.body.xDrive;
            float targetAngle = defaultTarget + (currentAction * drive.upperLimit);
            drive.target = targetAngle;
            part.body.xDrive = drive;

            previousActions[i] = currentAction;
            i++;
        }

        // コスト適用
        if (currentLesson > 0.5f)
        {
            AddReward(-totalEnergyCost * 0.05f);
        }

        // ★★★ 足ボーナス（交互運動 & 接地） Lesson 0, 1 限定 ★★★
        if (currentLesson < 1.5f)
        {
            // 1. 交互運動ボーナス
            if (leftThighBody != null && rightThighBody != null)
            {
                float velL = leftThighBody.jointVelocity[0];
                float velR = rightThighBody.jointVelocity[0];
                if (velL * velR < 0) AddReward(0.005f);
            }

            // 2. 足裏接地ボーナス
            bool isLeftGrounded = leftFootTransform != null && leftFootTransform.position.y < 0.15f;
            bool isRightGrounded = rightFootTransform != null && rightFootTransform.position.y < 0.15f;
            if (isLeftGrounded || isRightGrounded) AddReward(0.005f);
        }

        // 頭部安定化 (Lesson 0では無視)
        if (headTransform != null && headStabWeight > 0f)
        {
            float currentHeadY = headTransform.position.y;
            float heightError = Mathf.Abs(currentHeadY - targetHeadHeight);
            float positionReward = 1.0f / (1.0f + heightError * heightError);
            if (currentLesson > 0.5f) AddReward(positionReward * 0.01f);

            float verticalVelocity = (currentHeadY - prevHeadY) / Time.fixedDeltaTime;
            float bobbingPenalty = verticalVelocity * verticalVelocity * bobbingPenaltyWeight * headStabWeight;
            AddReward(-bobbingPenalty * 0.001f); 

            prevHeadY = currentHeadY;
        }

        if (target != null)
        {
            float currentDistance = Vector3.Distance(transform.position, target.position);
            
            // 速度報酬 (Lesson 0では強化)
            if (currentLesson < 0.5f)
            {
                 Vector3 dirToTarget = (target.position - transform.position).normalized;
                 float forwardVelocity = Vector3.Dot(rootArticulationBody.linearVelocity, dirToTarget);
                 if (forwardVelocity > 0) AddReward(forwardVelocity * 0.1f);
            }

            // 速度制限 (Lesson 3以降)
            if (isSpeedLimitActive)
            {
                float currentSpeed = rootArticulationBody.linearVelocity.magnitude;
                float speedError = Mathf.Abs(currentSpeed - targetWalkingSpeed);
                float speedReward = Mathf.Exp(-Mathf.Pow(speedError, 2));
                
                float distanceImprovement = initialTargetDistance - currentDistance;
                if (distanceImprovement > 0) AddReward(distanceImprovement * speedReward * 1.0f);
            }
            else
            {
                float distanceImprovement = initialTargetDistance - currentDistance;
                if (distanceImprovement > 0) AddReward(distanceImprovement * 1.0f);
            }
            
            initialTargetDistance = currentDistance;

            // 向き報酬
            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0; 
            float dotProduct = Vector3.Dot(transform.forward, toTarget.normalized);
            AddReward(dotProduct * 0.005f);

            // ゴール判定 (距離2.5m)
            if (currentDistance < 2.5f) 
            {
                OnTargetReached(10.0f);
            }
        }

        AddReward(0.001f);

        if (hasFallen)
        {
            AddReward(-1.0f);
            EndEpisode();
        }
        if (rootArticulationBody.transform.position.y < 1.0f) 
        {
            ReportFall("BodyHeight");
        }
    }

    public void OnTargetReached(float rewardAmount)
    {
        // Lesson 0ならボーナス増量
        if (currentLesson < 0.5f) rewardAmount *= 3.0f;

        AddReward(rewardAmount);
        
        if (currentLesson < 1.5f)
        {
            EndEpisode();
        }
        else 
        {
            float currentYAngle = transform.rotation.eulerAngles.y;
            SpawnTarget(currentYAngle);
            initialTargetDistance = Vector3.Distance(transform.position, target.position);
        }
    }

    public void ReportFall(string partName)
    {
        if (hasFallen) return;

        // 接地免除ロジック
        if (currentLesson < 0.5f)
        {
            if (partName.Contains("Head") || partName.Contains("Tail3")) return;
        }
        else if (currentLesson < 1.5f)
        {
            if (partName.Contains("Tail3")) return;
        }

        hasFallen = true;
        if (statsLogger != null) statsLogger.RecordFall(partName);
        AddReward(-1.0f);
        EndEpisode();
    }

    public void NotifyFall()
    {
        ReportFall("Unknown_HeightDrop");
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var contActionsOut = actionsOut.ContinuousActions;
        for (int i = 0; i < bodyParts.Length; i++)
        {
            contActionsOut[i] = 0f; 
        }
    }
}