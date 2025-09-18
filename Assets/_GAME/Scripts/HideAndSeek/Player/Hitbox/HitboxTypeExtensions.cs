// namespace _GAME.Scripts.HideAndSeek.Player.Hitbox
// {
//     /// <summary>
//     /// Extension methods cho enum operations
//     /// </summary>
//     public static class HitboxTypeExtensions
//     {
//         public static bool IsBodyPart(this HitboxType type)
//         {
//             return (type & (HitboxType.Head | HitboxType.Torso | HitboxType.LeftArm |
//                             HitboxType.RightArm | HitboxType.LeftLeg | HitboxType.RightLeg |
//                             HitboxType.LeftHand | HitboxType.RightHand | HitboxType.LeftFoot |
//                             HitboxType.RightFoot)) != 0;
//         }
//
//         public static bool IsVehiclePart(this HitboxType type)
//         {
//             return (type & (HitboxType.Engine | HitboxType.WheelFrontLeft | HitboxType.WheelFrontRight |
//                             HitboxType.WheelRearLeft | HitboxType.WheelRearRight | HitboxType.WindowFront |
//                             HitboxType.WindowRear | HitboxType.WindowLeft | HitboxType.WindowRight |
//                             HitboxType.DoorLeft | HitboxType.DoorRight | HitboxType.Hood | HitboxType.Trunk)) != 0;
//         }
//
//         public static bool IsWheel(this HitboxType type)
//         {
//             return (type & (HitboxType.WheelFrontLeft | HitboxType.WheelFrontRight |
//                             HitboxType.WheelRearLeft | HitboxType.WheelRearRight)) != 0;
//         }
//
//         public static bool IsWindow(this HitboxType type)
//         {
//             return (type & (HitboxType.WindowFront | HitboxType.WindowRear |
//                             HitboxType.WindowLeft | HitboxType.WindowRight | HitboxType.Window)) != 0;
//         }
//
//         public static bool IsCritical(this HitboxType type)
//         {
//             return type.HasFlag(HitboxType.Critical) ||
//                    type == HitboxType.Head ||
//                    type == HitboxType.Engine;
//         }
//
//         public static bool IsArmored(this HitboxType type)
//         {
//             return type.HasFlag(HitboxType.Armored);
//         }
//
//         public static bool IsPenetrable(this HitboxType type)
//         {
//             return type.HasFlag(HitboxType.Penetrable) ||
//                    type.IsWindow();
//         }
//
//         public static HitboxCategory GetDefaultCategory(this HitboxType type)
//         {
//             if (type.IsBodyPart())
//             {
//                 if (type == HitboxType.Head) return HitboxCategory.VitalOrgan;
//                 if (type == HitboxType.Torso) return HitboxCategory.VitalOrgan;
//                 return HitboxCategory.Limb;
//             }
//
//             if (type.IsVehiclePart())
//             {
//                 if (type == HitboxType.Engine) return HitboxCategory.CriticalComponent;
//                 if (type.IsWheel()) return HitboxCategory.Mobility;
//                 if (type.IsWindow()) return HitboxCategory.Glass;
//                 return HitboxCategory.VehicleComponent;
//             }
//
//             return HitboxCategory.None;
//         }
//
//         public static float GetDefaultDamageMultiplier(this HitboxType type)
//         {
//             if (type == HitboxType.Head) return 2.5f;
//             if (type == HitboxType.Torso) return 1.0f;
//             if (type.IsBodyPart()) return 0.8f; // Limbs
//
//             if (type == HitboxType.Engine) return 1.5f;
//             if (type.IsWheel()) return 1.2f;
//             if (type.IsWindow()) return 0.5f;
//
//             return 1.0f;
//         }
//
//         public static float GetDefaultArmorValue(this HitboxType type)
//         {
//             if (type.HasFlag(HitboxType.Armored)) return 25f;
//             if (type == HitboxType.Engine) return 20f;
//             if (type == HitboxType.Hood || type == HitboxType.DoorLeft || type == HitboxType.DoorRight) return 15f;
//             if (type.IsWheel()) return 5f;
//
//             return 0f;
//         }
//     }
//
//
//     /// <summary>
//     /// Helper class để setup hitbox từ enum
//     /// </summary>
//     public static class HitboxSetupHelper
//     {
//         /// <summary>
//         /// Tạo HitboxInfo với default values từ enum
//         /// </summary>
//         public static HitBoxInfo CreateFromType(HitboxType type, string customId = "")
//         {
//             return new HitBoxInfo(
//                 type: type,
//                 cat: type.GetDefaultCategory(),
//                 custom: customId,
//                 multiplier: type.GetDefaultDamageMultiplier(),
//                 armor: type.GetDefaultArmorValue(),
//                 canTake: true,
//                 penetrable: type.IsPenetrable(),
//                 specialEffect: type.IsCritical(),
//                 effectId: type.IsCritical() ? GetDefaultSpecialEffect(type) : ""
//             );
//         }
//
//         /// <summary>
//         /// Tạo combined hitbox type (ví dụ: Head | Critical | Armored)
//         /// </summary>
//         public static HitBoxInfo CreateCombined(HitboxType baseType, HitboxType modifiers, string customId = "")
//         {
//             var combinedType = baseType | modifiers;
//             return CreateFromType(combinedType, customId);
//         }
//
//         private static string GetDefaultSpecialEffect(HitboxType type)
//         {
//             if (type == HitboxType.Head) return "headshot";
//             if (type == HitboxType.Engine) return "engine_damage";
//             if (type.IsWindow()) return "glass_break";
//             if (type.IsBodyPart() && type != HitboxType.Head && type != HitboxType.Torso) return "limb_damage";
//
//             return "";
//         }
//
//         /// <summary>
//         /// Setup auto cho humanoid
//         /// </summary>
//         public static HitBoxInfo[] GetHumanoidHitboxes()
//         {
//             return new HitBoxInfo[]
//             {
//                 CreateFromType(HitboxType.Head),
//                 CreateFromType(HitboxType.Torso),
//                 CreateFromType(HitboxType.LeftArm),
//                 CreateFromType(HitboxType.RightArm),
//                 CreateFromType(HitboxType.LeftLeg),
//                 CreateFromType(HitboxType.RightLeg),
//                 CreateFromType(HitboxType.LeftHand),
//                 CreateFromType(HitboxType.RightHand),
//                 CreateFromType(HitboxType.LeftFoot),
//                 CreateFromType(HitboxType.RightFoot)
//             };
//         }
//
//         /// <summary>
//         /// Setup auto cho vehicle
//         /// </summary>
//         public static HitBoxInfo[] GetVehicleHitboxes()
//         {
//             return new HitBoxInfo[]
//             {
//                 CreateFromType(HitboxType.Engine),
//                 CreateFromType(HitboxType.WheelFrontLeft),
//                 CreateFromType(HitboxType.WheelFrontRight),
//                 CreateFromType(HitboxType.WheelRearLeft),
//                 CreateFromType(HitboxType.WheelRearRight),
//                 CreateFromType(HitboxType.WindowFront),
//                 CreateFromType(HitboxType.WindowRear),
//                 CreateFromType(HitboxType.WindowLeft),
//                 CreateFromType(HitboxType.WindowRight),
//                 CreateFromType(HitboxType.DoorLeft | HitboxType.Armored), // Armored door
//                 CreateFromType(HitboxType.DoorRight | HitboxType.Armored),
//                 CreateFromType(HitboxType.Hood),
//                 CreateFromType(HitboxType.Trunk)
//             };
//         }
//     }
// }