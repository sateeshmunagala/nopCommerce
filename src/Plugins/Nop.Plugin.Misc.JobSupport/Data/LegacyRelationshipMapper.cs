using Nop.Plugin.Misc.JobSupport.Contracts;

namespace Nop.Plugin.Misc.JobSupport.Data;

// Compatibility retained for read rollback; remove in JobSupport 2.0.0.
public static class LegacyRelationshipMapper
{
    public static bool TryMapToLegacyCartType(RelationshipType relationshipType, out int cartTypeId)
    {
        cartTypeId = relationshipType switch
        {
            RelationshipType.ShortlistedByMe => 2,
            RelationshipType.ShortlistedMe => 3,
            RelationshipType.InterestSent => 4,
            RelationshipType.InterestReceived => 5,
            RelationshipType.AcceptedByMe => 6,
            RelationshipType.AcceptedMe => 7,
            RelationshipType.DeclinedByMe => 8,
            RelationshipType.DeclinedMe => 9,
            RelationshipType.BlockedByMe => 10,
            RelationshipType.BlockedMe => 11,
            RelationshipType.ViewedByMe => 12,
            RelationshipType.ViewedMe => 13,
            _ => 0
        };

        return cartTypeId > 0;
    }
}
