using System.Linq;
using UnityEngine;
using Waterfall;

public class UnifiedWaterfallControlModule : PartModule
{
    Transform tt10, tt11, tt12, tt13;


    ModuleWaterfallFX[] waterFX;
    ModuleEnginesFX engineInner;
    ModuleEnginesFX engineCore;

    float lastVerticalSpeed;
    float lastAltitude;
    float ascendHeightAccum;

    float decelStartSpeed;

    float landingBurnInnerTimer;
    float landingBurnCoreTimer;

    bool innerTriggered;
    bool coreTriggered;

    float innerIgniteTime = -1f;
    float coreIgniteTime = -1f;

    bool downVelArmed;

    enum DownVelMode { None, Decel }
    DownVelMode mode = DownVelMode.None;

    const float RAMP_TIME = 2f;
    const float SYNC_WINDOW = 0.5f;
    const float INNER_DELAY = 0.5f;

    public override void OnStart(StartState state)
    {
        tt10 = part.FindModelTransform("thrustTransform10");
        tt11 = part.FindModelTransform("thrustTransform11");
        tt12 = part.FindModelTransform("thrustTransform12");
        tt13 = part.FindModelTransform("thrustTransform13");

        waterFX = part.FindModulesImplementing<ModuleWaterfallFX>().ToArray();

        var engines = part.FindModulesImplementing<ModuleEnginesFX>();
        engineInner = engines.FirstOrDefault(e => e.engineID == "Inner");
        engineCore = engines.FirstOrDefault(e => e.engineID == "Core");
    }

    public void FixedUpdate()
    {
        if (!HighLogic.LoadedSceneIsFlight || vessel == null) return;

        Vector3d upD = vessel.transform.position - vessel.mainBody.position;
        Vector3 worldUp = ((Vector3)upD).normalized;

        Vector3 vel = (Vector3)vessel.obt_velocity;
        float verticalSpeed = Vector3.Dot(vel, worldUp);
        float verticalAccel =
            (verticalSpeed - lastVerticalSpeed) / Time.fixedDeltaTime;
        lastVerticalSpeed = verticalSpeed;

        bool ascending = verticalSpeed > 0.1f;
        bool descending = verticalSpeed < -0.1f;

        float altitude = (float)vessel.altitude;
        if (ascending)
            ascendHeightAccum += Mathf.Max(0f, altitude - lastAltitude);
        else
            ascendHeightAccum = 0f;

        lastAltitude = altitude;
        dbgAscendHeight = ascendHeightAccum;

        upndown = ascending ? 1 : descending ? -1 : 0;

        if (tt10) dbgTT10 = CalcAngle(tt10);
        if (tt11) dbgTT11 = CalcAngle(tt11);
        if (tt12) dbgTT12 = CalcAngle(tt12);
        if (tt13) dbgTT13 = CalcAngle(tt13);

        if (!descending || ascendHeightAccum > 70f)
        {
            ResetAll();
            PushAll();
            return;
        }

        if (verticalAccel > 0f && mode == DownVelMode.None)
        {
            mode = DownVelMode.Decel;
            decelStartSpeed = (float)vessel.srfSpeed;
        }

        if (mode == DownVelMode.Decel)
        {
            dbgState = "DECEL";
            downdown = 1f;

            float srfSpeed = (float)vessel.srfSpeed;

            if (srfSpeed <= 5f)
            {
                downVelocity = 0f;
            }
            else if (srfSpeed <= 50f)
            {
                downVelocity = 0.1f;
            }
            else
            {
                float t = Mathf.InverseLerp(
                    decelStartSpeed,
                    50f,
                    srfSpeed
                );
                downVelocity = Mathf.Lerp(1f, 0.1f, t);
            }
        }

        bool inDownVelRange = downVelocity >= 0.1f && downVelocity <= 1f;
        if (inDownVelRange && !downVelArmed)
            downVelArmed = true;

        if (engineCore != null && engineCore.EngineIgnited && coreIgniteTime < 0f)
            coreIgniteTime = Time.time;

        if (engineInner != null && engineInner.EngineIgnited && innerIgniteTime < 0f)
            innerIgniteTime = Time.time;

        bool simultaneous =
            coreIgniteTime > 0f &&
            innerIgniteTime > 0f &&
            Mathf.Abs(coreIgniteTime - innerIgniteTime) <= SYNC_WINDOW;

        if (!coreTriggered &&
            engineCore != null &&
            engineCore.EngineIgnited &&
            engineCore.currentThrottle > 0f &&
            downVelArmed)
        {
            landingBurnCoreTimer += Time.fixedDeltaTime;
            float t = landingBurnCoreTimer / RAMP_TIME;
            landingBurnCore = Mathf.Lerp(0f, 2f, t);

            if (t >= 1f)
            {
                landingBurnCore = 0f;
                coreTriggered = true;
            }
        }

        bool allowInner =
            !simultaneous ||
            (coreIgniteTime > 0f && Time.time - coreIgniteTime >= INNER_DELAY);

        if (!innerTriggered &&
            allowInner &&
            engineInner != null &&
            engineInner.EngineIgnited &&
            engineInner.currentThrottle > 0f &&
            downVelArmed)
        {
            landingBurnInnerTimer += Time.fixedDeltaTime;
            float t = landingBurnInnerTimer / RAMP_TIME;
            landingBurnInner = Mathf.Lerp(0f, 2f, t);

            if (t >= 1f)
            {
                landingBurnInner = 0f;
                innerTriggered = true;
            }
        }

        if (engineCore != null && !engineCore.EngineIgnited)
            ResetCore();

        if (engineInner != null && !engineInner.EngineIgnited)
            ResetInner();

        PushAll();
    }

    float CalcAngle(Transform t)
    {
        Vector3 worldDir = t.forward.normalized;
        Vector3d upD = part.transform.position - vessel.mainBody.position;

        Quaternion invRot = Quaternion.Inverse(part.transform.rotation);
        Vector3 dirNoRot = invRot * worldDir;
        Vector3 upNoRot = invRot * ((Vector3)upD).normalized;

        return Mathf.Asin(Vector3.Dot(dirNoRot, upNoRot)) * Mathf.Rad2Deg;
    }

    void ResetCore()
    {
        landingBurnCore = 0f;
        landingBurnCoreTimer = 0f;
        coreTriggered = false;
        coreIgniteTime = -1f;
        downVelArmed = false;
    }

    void ResetInner()
    {
        landingBurnInner = 0f;
        landingBurnInnerTimer = 0f;
        innerTriggered = false;
        innerIgniteTime = -1f;
        downVelArmed = false;
    }

    void ResetAll()
    {
        ResetCore();
        ResetInner();
        downVelocity = 0f;
        downdown = 0f;
        mode = DownVelMode.None;
    }

    void PushAll()
    {
        foreach (var fx in waterFX)
        {
            fx.Controllers.FirstOrDefault(c => c.name == "TT10")?.Set(dbgTT10);
            fx.Controllers.FirstOrDefault(c => c.name == "TT11")?.Set(dbgTT11);
            fx.Controllers.FirstOrDefault(c => c.name == "TT12")?.Set(dbgTT12);
            fx.Controllers.FirstOrDefault(c => c.name == "TT13")?.Set(dbgTT13);

            fx.Controllers.FirstOrDefault(c => c.name == "upndown")?.Set(upndown);
            fx.Controllers.FirstOrDefault(c => c.name == "downdown")?.Set(downdown);
            fx.Controllers.FirstOrDefault(c => c.name == "downVelocity")?.Set(downVelocity);

            fx.Controllers.FirstOrDefault(c => c.name == "LandingBurnCore")?.Set(landingBurnCore);
            fx.Controllers.FirstOrDefault(c => c.name == "LandingBurnInner")?.Set(landingBurnInner);
        }
    }
}
