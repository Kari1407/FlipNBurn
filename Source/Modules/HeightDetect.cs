using System;
using System.Linq;
using System.Text;
using UnityEngine;
using Waterfall;

namespace HeightDetect
{
    public class HeightDetectModule : PartModule
    {
        [KSPField] public string terrainControllerName = "Height";
        [KSPField] public string waterControllerName = "Water";

        [KSPField] public string targetTransformName = "thrustTransform1";
        [KSPField] public string followTransformName = "thrustTransform3";

        [KSPField(guiActive = true)] public string dbgHitType = "None";
        [KSPField(guiActive = true)] public string dbgHitName = "None";
        [KSPField(guiActive = true, guiFormat = "F2")] public float dbgHitDist = 0f;
        [KSPField(guiActive = true, guiFormat = "F2")] public float dbgFinalHeight = 0f;
        [KSPField(guiActive = true)] public int dbgBelowSolid = 0;
        [KSPField(guiActive = true)] public int dbgLaunchPad = 0;
        [KSPField(guiActive = true)] public string dbgAllHits = "None";

        private Transform targetTransform;
        private Transform followTransform;
        private ModuleWaterfallFX[] waterFX;

        const float RAY_MAX = 100000f;
        const float HEIGHT_MAX = 300f;
        const float SECONDARY_OFFSET = 0.05f;
        const float BELOW_RAY = 100f;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            targetTransform = part.FindModelTransform(targetTransformName);
            followTransform = part.FindModelTransform(followTransformName);

            waterFX = part.FindModulesImplementing<ModuleWaterfallFX>().ToArray();
        }

        private bool IsRealSolid(Collider c)
        {
            if (c == null) return false;
            if (c.isTrigger) return false;
            var go = c.gameObject;
            return go.GetComponent<MeshRenderer>() != null || go.GetComponent<MeshFilter>() != null;
        }

        private bool IsEarthCollider(Collider c)
        {
            if (c == null) return false;
            string n = c.gameObject.name.ToLower();
            return n.Contains("earth") || n.Contains("pqs") || n.Contains("terrain");
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (vessel == null) return;
            if (targetTransform == null) return;

            // reset debug
            dbgHitType = "None";
            dbgHitName = "None";
            dbgHitDist = -1f;
            dbgBelowSolid = 0;
            dbgLaunchPad = 0;
            dbgAllHits = "None";

            // downward raycast (from thrustTransform1 straight down)
            RaycastHit[] hits = Physics.RaycastAll(targetTransform.position, -targetTransform.up, RAY_MAX, ~0);
            if (hits.Length > 0)
            {
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                var sb = new StringBuilder();
                foreach (var h in hits) if (h.collider != null) sb.Append(h.collider.gameObject.name).Append(", ");
                dbgAllHits = sb.Length > 0 ? sb.ToString().TrimEnd(',', ' ') : "None";
            }

            // find first real solid hit, with Earth special handling (the "working" method)
            RaycastHit chosen = new RaycastHit();
            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                if (!IsRealSolid(h.collider)) continue;

                // if it's Earth, perform secondary short ray from hit.point + offset downwards to see if there's a real object below
                if (IsEarthCollider(h.collider))
                {
                    Vector3 secStart = h.point + (-targetTransform.up * SECONDARY_OFFSET);
                    RaycastHit sec;
                    if (Physics.Raycast(secStart, -targetTransform.up, out sec, BELOW_RAY, ~0))
                    {
                        if (IsRealSolid(sec.collider))
                        {
                            // Earth 下有实体 -> use sec
                            chosen = sec;
                            dbgBelowSolid = 1;
                            found = true;
                            break;
                        }
                    }
                    // Earth 下没有真实实体 -> use Earth hit itself (this is the working behavior you wanted)
                    chosen = h;
                    dbgBelowSolid = 0;
                    found = true;
                    break;
                }
                else
                {
                    chosen = h;
                    dbgBelowSolid = 0;
                    found = true;
                    break;
                }
            }

            float finalHeight;

            // water detection (original working logic)
            bool isWater = false;
            if (vessel.mainBody.ocean)
            {
                double pqs = vessel.terrainAltitude;
                if (pqs <= 1.0) isWater = true;
            }

            if (isWater)
            {
                finalHeight = (float)vessel.radarAltitude;
                dbgHitType = "Water";
                dbgHitName = "Ocean";
                dbgHitDist = finalHeight;
            }
            else if (found)
            {
                finalHeight = chosen.distance;
                dbgHitType = "Solid";
                dbgHitName = chosen.collider != null ? chosen.collider.gameObject.name : "Unknown";
                dbgHitDist = chosen.distance;

                string lname = dbgHitName.ToLower();
                if (lname.Contains("launchpad") || lname.Contains("launch pad") || lname.Contains("lp"))
                    dbgLaunchPad = 1;
            }
            else
            {
                finalHeight = (float)vessel.radarAltitude;
                dbgHitType = "RadarAlt";
                dbgHitName = "None";
                dbgHitDist = finalHeight;
            }

            if (finalHeight > HEIGHT_MAX) finalHeight = HEIGHT_MAX;
            dbgFinalHeight = finalHeight;

            // write to Waterfall controllers (KSP1 style)
            foreach (var fx in waterFX)
            {
                var ctrH = fx.Controllers.FirstOrDefault(c => c.name == terrainControllerName);
                if (ctrH != null) ctrH.Set(finalHeight);

                var ctrW = fx.Controllers.FirstOrDefault(c => c.name == waterControllerName);
                if (ctrW != null) ctrW.Set(isWater ? 1f : 0f);
            }

            // === thrustTransform3 behavior ===
            if (followTransform != null)
            {
                // 1) 位置：保持你之前想要的 world 位置计算（T1 沿其 down * finalHeight）
                Vector3 groundWorld = targetTransform.position - targetTransform.up * finalHeight;

                // 2) 转换为 part.localPosition（你之前要的：把 world 映到 part.local）
                Vector3 localPos = part.transform.InverseTransformPoint(groundWorld);
                followTransform.localPosition = localPos;

                // 3) 朝上方向：**如果命中了真实实体就用 chosen.normal（相对于命中物的表面法线），否则用世界向上**
                Vector3 upDir = Vector3.up;
                if (found && chosen.collider != null)
                {
                    upDir = chosen.normal; // 使用命中表面的法线
                }
                else if (isWater)
                {
                    upDir = Vector3.up;
                }
                // 将 followTransform 的 up 指向 upDir（保持朝上相对于命中表面）
                if (upDir.sqrMagnitude > 1e-6f)
                {
                    // 保证 forward 不是零：用 tStart.forward 投影到面切线作为 forward 参考
                    Vector3 forwardRef = Vector3.ProjectOnPlane(targetTransform.forward, upDir);
                    if (forwardRef.sqrMagnitude < 1e-6f)
                        forwardRef = Vector3.ProjectOnPlane(part.transform.forward, upDir);
                    if (forwardRef.sqrMagnitude < 1e-6f)
                        forwardRef = Vector3.forward;

                    followTransform.rotation = Quaternion.LookRotation(forwardRef.normalized, upDir.normalized);
                }
            }
        }
    }
}
