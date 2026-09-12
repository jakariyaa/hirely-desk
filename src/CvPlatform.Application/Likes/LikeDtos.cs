namespace CvPlatform.Application.Likes;

public sealed record LikeStateDto(Guid CvId, bool LikedByMe, int LikeCount);
