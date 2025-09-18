using System;

namespace _GAME.Scripts.HideAndSeek.Player.HitBox
{
    /// <summary>
    /// Enum cho các loại hitbox - có thể mở rộng theo từng game type
    /// </summary>
    [Flags]
    public enum HitBoxType
    {
        // Base categories
        None = 0,
        
        // Human body parts
        Head = 1 << 0,
        Torso = 1 << 1,
        LeftArm = 1 << 2,
        RightArm = 1 << 3,
        LeftLeg = 1 << 4,
        RightLeg = 1 << 5,
        LeftHand = 1 << 6,
        RightHand = 1 << 7,
        LeftFoot = 1 << 8,
        RightFoot = 1 << 9,
        
        // Vehicle components
        Engine = 1 << 10,
        WheelFrontLeft = 1 << 11,
        WheelFrontRight = 1 << 12,
        WheelRearLeft = 1 << 13,
        WheelRearRight = 1 << 14,
        WindowFront = 1 << 15,
        WindowRear = 1 << 16,
        WindowLeft = 1 << 17,
        WindowRight = 1 << 18,
        DoorLeft = 1 << 19,
        DoorRight = 1 << 20,
        Hood = 1 << 21,
        Trunk = 1 << 22,
        
        // Building parts
        Wall = 1 << 23,
        Door = 1 << 24,
        Window = 1 << 25,
        Roof = 1 << 26,
        Foundation = 1 << 27,
        
        // Generic categories (combinable with specific types)
        Critical = 1 << 28,
        Armored = 1 << 29,
        Penetrable = 1 << 30,
        Destructible = 1 << 31,
    }
    
    /// <summary>
    /// Categories cho nhóm hitbox - dùng enum thay vì string
    /// </summary>
    public enum HitBoxCategory
    {
        None,
        BodyPart,
        VitalOrgan,
        Limb,
        VehicleComponent,
        CriticalComponent,
        Mobility,
        Armor,
        Glass,
        Structure,
        Destructible,
        Environment
    }

}