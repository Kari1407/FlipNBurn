using System;
using System.Linq;
using UnityEngine;
using Waterfall;

public class DelugeWaterfallController : PartModule
{

    // 三段式下降参数
    private const float stopPoint = 0.75f;

    private const float slowSectionDuration = 2f;  // 第一段：1 → 0.75
    private const float pauseDuration = 2f;        // 第二段：保持 0.75
    private const float finalDropDuration = 3f;    // 第三段：0.75 → 0

    // ★ 防止未启动时就下降
    private bool hasEverActivated = false;

    public override void OnStart(StartState state)
    {
        base.OnStart(state);

        if (string.IsNullOrEmpty(engineID))
            engine = part.FindModuleImplementing<ModuleEnginesFX>();
        else
            engine = part.Modules.OfType<ModuleEnginesFX>()
                    .FirstOrDefault(e => e.engineID == engineID);

        waterfalls = part.FindModulesImplementing<ModuleWaterfallFX>().ToArray();
    }

    public void Update()
    {
        if (engine == null || waterfalls == null)
            return;

        bool activeNow = engine.finalThrust > 0.01f;

        // ================================
        // 引擎开启 → 上升
        // ================================
        if (activeNow)
        {
            hasEverActivated = true;   // ★ 引擎首次真正开启
            isShuttingDown = false;
            shutdownTimer = 0f;

            if (currentValue < 1f)
            {
                currentValue += Time.deltaTime / rampUpTime;
                if (currentValue > 1f) currentValue = 1f;
            }
        }
        else
        {
            // ★ 从未开启过引擎，不允许下降逻辑
            if (!hasEverActivated)
            {
                currentValue = 0f;
                ApplyToWaterfall();
                return;
            }

            // ================================
            // 三段式下降
            // ================================
            if (!isShuttingDown)
            {
                isShuttingDown = true;
                shutdownTimer = 0f;
            }

            shutdownTimer += Time.deltaTime;

            if (shutdownTimer <= slowSectionDuration)
            {
                // 第一段：1 → 0.75
                float t = shutdownTimer / slowSectionDuration;
                currentValue = Mathf.Lerp(1f, stopPoint, t);
            }
            else if (shutdownTimer <= slowSectionDuration + pauseDuration)
            {
                // 第二段：停留在 0.75
                currentValue = stopPoint;
            }
            else
            {
                // 第三段：0.75 → 0
                float t = (shutdownTimer - slowSectionDuration - pauseDuration) / finalDropDuration;
                currentValue = Mathf.Lerp(stopPoint, 0f, t);
                if (currentValue < 0f) currentValue = 0f;
            }
        }

        ApplyToWaterfall();
    }

    private void ApplyToWaterfall()
    {
        foreach (var wf in waterfalls)
            wf.SetControllerValue(controllerName, currentValue);
    }
}
