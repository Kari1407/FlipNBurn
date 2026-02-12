using System.Linq;
using UnityEngine;
using Waterfall;

//
// ================= PRODUCER =================
//

public class LiftoffEngineProducer : PartModule
{
    ModuleEnginesFX[] engines;
    ModuleWaterfallFX[] fx;

    float upValue;
    float downValue;

    float downTimer;
    bool downStarted;
    bool downDone;

    // ===== PAW DEBUG =====

    [KSPField(guiActive = true)] public float dbgUp;
    [KSPField(guiActive = true)] public float dbgDown;
    [KSPField(guiActive = true)] public float dbgThrust;
    [KSPField(guiActive = true)] public string dbgState;

    public override void OnStart(StartState s)
    {
        engines = part.FindModulesImplementing<ModuleEnginesFX>().ToArray();
        fx = part.FindModulesImplementing<ModuleWaterfallFX>().ToArray();
        dbgState = "IDLE";
    }

    public void FixedUpdate()
    {
        if (!HighLogic.LoadedSceneIsFlight) return;

        float dt = Time.fixedDeltaTime;

        // ===== 推力总和 =====
        float thrust = 0f;
        for (int i = 0; i < engines.Length; i++)
            thrust += engines[i].finalThrust;

        bool hasThrust = thrust > 0.5f;

        // =========================
        // UP：只要有推力就推进
        // =========================

        if (!downStarted && hasThrust)
        {
            upValue += dt;

            if (upValue > 150f)
                upValue = 150f;

            dbgState = "UP";
        }

        // =========================
        // DOWN 触发条件
        // up>0 且 无推力
        // =========================

        if (!downStarted && upValue > 0f && !hasThrust)
        {
            downStarted = true;
            downTimer = 0f;
            dbgState = "DOWN";
        }

        // =========================
        // DOWN 执行
        // =========================

        if (downStarted && !downDone)
        {
            downTimer += dt;

            downValue = Mathf.Lerp(1f, 30f, downTimer / 30f);

            if (downTimer >= 30f)
            {
                downValue = 0f;
                upValue = 0f;
                downDone = true;
                dbgState = "DONE";
            }
        }

        // =========================
        // 写共享
        // =========================

        LiftoffSharedData.Up = upValue;
        LiftoffSharedData.Down = downValue;
        LiftoffSharedData.Thrust = thrust;
        LiftoffSharedData.ProducerPos = part.transform.position;

        // =========================
        // PAW
        // =========================

        dbgUp = upValue;
        dbgDown = downValue;
        dbgThrust = thrust;

        // =========================
        // Waterfall 推送（本部件）
        // =========================

        for (int i = 0; i < fx.Length; i++)
        {
            fx[i].Controllers.FirstOrDefault(c => c.name == "liftoff time")?.Set(upValue);
            fx[i].Controllers.FirstOrDefault(c => c.name == "liftoff down")?.Set(downValue);
            fx[i].Controllers.FirstOrDefault(c => c.name == "ClusterPower")?.Set(thrust);
        }
    }
}

//
// ================= CONSUMER =================
//

public class LiftoffConsumer : PartModule
{
    ModuleWaterfallFX[] fx;


    public override void OnStart(StartState s)
    {
        fx = part.FindModulesImplementing<ModuleWaterfallFX>().ToArray();
    }

    public void FixedUpdate()
    {
        if (!HighLogic.LoadedSceneIsFlight) return;

        float up = LiftoffSharedData.Up;
        float down = LiftoffSharedData.Down;
        float thrust = LiftoffSharedData.Thrust;

        float dist = Vector3.Distance(
            part.transform.position,
            LiftoffSharedData.ProducerPos
        );

        // ===== PAW =====

        cUp = up;
        cDown = down;
        cThrust = thrust;
        cDistance = dist;

        // ===== Waterfall =====

        for (int i = 0; i < fx.Length; i++)
        {
            fx[i].Controllers.FirstOrDefault(c => c.name == "liftoff time")?.Set(up);
            fx[i].Controllers.FirstOrDefault(c => c.name == "liftoff down")?.Set(down);
            fx[i].Controllers.FirstOrDefault(c => c.name == "ClusterPower")?.Set(thrust);
            fx[i].Controllers.FirstOrDefault(c => c.name == "distance")?.Set(dist);
        }
    }
}
