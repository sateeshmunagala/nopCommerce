using Nop.Plugin.Misc.JobSupport.Contracts;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial interface IJobSupportRelationshipService
{
    Task<RelationshipActionResult> ShortlistProfileAsync(int sourceCustomerId, int targetProfileId);
    Task<RelationshipActionResult> RemoveShortlistAsync(int sourceCustomerId, int targetProfileId);
    Task<RelationshipActionResult> SendInterestAsync(int sourceCustomerId, int targetProfileId);
    Task<RelationshipActionResult> AcceptInterestAsync(int sourceCustomerId, int relationshipSourceCustomerId);
    Task<RelationshipActionResult> DeclineInterestAsync(int sourceCustomerId, int relationshipSourceCustomerId);
    Task<RelationshipActionResult> BlockProfileAsync(int sourceCustomerId, int targetProfileId);
    Task<RelationshipActionResult> RecordProfileViewAsync(int sourceCustomerId, int targetProfileId);
}
