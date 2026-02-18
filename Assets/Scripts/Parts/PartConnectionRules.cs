public static class PartConnectionRules
{
    public static bool IsNodeKindAllowedOnPart(PartType partType, NodeKind nodeKind)
    {
        switch (partType)
        {
            case PartType.Core:
            case PartType.Frame:
                return nodeKind == NodeKind.FrameMount
                    || nodeKind == NodeKind.MotorMount
                    || nodeKind == NodeKind.WheelMount
                    || nodeKind == NodeKind.CargoMount
                    || nodeKind == NodeKind.Generic;

            case PartType.Motor:
                return nodeKind == NodeKind.FrameMount
                    || nodeKind == NodeKind.MotorMount
                    || nodeKind == NodeKind.WheelMount
                    || nodeKind == NodeKind.Generic;

            case PartType.Wheel:
                return nodeKind == NodeKind.WheelHub
                    || nodeKind == NodeKind.Generic;

            case PartType.Cargo:
            case PartType.Ballast:
                return nodeKind == NodeKind.CargoMount
                    || nodeKind == NodeKind.FrameMount
                    || nodeKind == NodeKind.Generic;

            default:
                return true;
        }
    }

    public static bool IsConnectionAllowed(PartType aPartType, NodeKind aNodeKind, PartType bPartType, NodeKind bNodeKind)
    {
        if (!IsNodeKindAllowedOnPart(aPartType, aNodeKind)) return false;
        if (!IsNodeKindAllowedOnPart(bPartType, bNodeKind)) return false;
        return true;
    }
}
