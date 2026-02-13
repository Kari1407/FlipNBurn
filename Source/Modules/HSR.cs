using System;
using System.Linq;
using UnityEngine;
using Waterfall;

namespace BoosterThrustDetect
{
    public class BoosterThrustDetectModule : PartModule
    {
        // ================= CFG =================

        [KSPField] public string detectTransformName = "detectTransform";
        [KSPField] public string numberControllerName = "Number";
        [KSPField] public string pushControllerName = "Push";

        // ⚠️ 只允许这些引擎
        [KSPField]
        public string allowedEngineParts = "FNB_R3_VAC,FNB_R3_CENTER";

        [KSPField(guiActive = true, isPersistant = true)]
        [UI_FloatRange(minValue = 0.3f, maxValue = 0.95f, stepIncrement = 0.05f)]
        public float facingDotThreshold = 0.6f;

        [KSPField(guiActive = true, isPersistant = true)]
        [UI_FloatRange(minValue = 5f, maxValue = 150f, stepIncrement = 5f)]
        public float maxDistance = 60f;

        [KSPField]
        public float thrustScale = 0.001f;

        // ================= Debug =================

        [KSPField(guiActive = true)] public int dbgEngineCount = 0;
        [KSPField(guiActive = true, guiFormat = "F1")] public float dbgTotalThrust = 0f;
        [KSPField(guiActive = true, guiFormat = "F2")] public float dbgScaledPush = 0f;
        [KSPField(guiActive = true)] public string dbgLastEngine = "None";
        [KSPField(guiActive = true, guiFormat = "F2")] public float dbgMaxDot = -1f;
        [KSPField(guiActive = true, guiFormat = "F1")] public float dbgClosestDistance = -1f;

        // ================= 内部 =================

        private Transform detectTransform;
        private ModuleWaterfallFX[] waterFX;
        private string[] allowedEngines;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            detectTransform = part.FindModelTransform(detectTransformName);
            waterFX = part.FindModulesImplementing<ModuleWaterfallFX>().ToArray();

            allowedEngines = allowedEngineParts
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToArray();
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null) return;
            if (detectTransform == null) return;

            int engineCount = 0;
            float totalThrust = 0f;
            float bestDot = -1f;
            float closestDist = float.MaxValue;
            string lastEngine = "None";

            foreach (var p in vessel.parts)
            {
                // ❌ 非指定引擎直接跳过
                if (!allowedEngines.Contains(p.partInfo.name))
                    continue;

                foreach (var engine in p.FindModulesImplementing<ModuleEngines>())
                {
                    if (!engine.EngineIgnited) continue;
                    if (engine.finalThrust <= 0f) continue;

                    bool counted = false;

                    foreach (var t in engine.thrustTransforms)
                    {
                        Vector3 enginePos = t.position;
                        Vector3 engineDir = t.forward; // 若反了改成 -t.forward

                        Vector3 dirToDetector =
                            (detectTransform.position - enginePos).normalized;

                        float dot = Vector3.Dot(engineDir.normalized, dirToDetector);
                        float dist = Vector3.Distance(enginePos, detectTransform.position);

                        if (dot > bestDot) bestDot = dot;
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            lastEngine = p.partInfo.title;
                        }

                        if (dot < facingDotThreshold) continue;
                        if (dist > maxDistance) continue;

                        if (!counted)
                        {
                            engineCount++;
                            totalThrust += engine.finalThrust;
                            counted = true;
                        }
                    }
                }
            }

            float scaledPush = totalThrust * thrustScale;

            // Debug
            dbgEngineCount = engineCount;
            dbgTotalThrust = totalThrust;
            dbgScaledPush = scaledPush;
            dbgLastEngine = lastEngine;
            dbgMaxDot = bestDot;
            dbgClosestDistance = closestDist == float.MaxValue ? -1f : closestDist;

            // 写入 Waterfall
            foreach (var fx in waterFX)
            {
                var ctrN = fx.Controllers.FirstOrDefault(c => c.name == numberControllerName);
                if (ctrN != null) ctrN.Set(engineCount);

                var ctrP = fx.Controllers.FirstOrDefault(c => c.name == pushControllerName);
                if (ctrP != null) ctrP.Set(scaledPush);
            }
        }
    }
}
