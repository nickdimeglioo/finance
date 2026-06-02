using System;

namespace PipelineRunner.Services
{
    public interface IExecutionContext
    {
        Guid? OrganizationId { get; set; }
        Guid? ProjectId { get; set; }
        Guid? UserId { get; set; }
        void Set(Guid? orgId, Guid? projectId, Guid? userId);
        void Clear();
    }
}
