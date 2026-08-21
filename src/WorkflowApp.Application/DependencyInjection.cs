using Microsoft.Extensions.DependencyInjection;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Identity.Services;
using WorkflowApp.Application.Requests.Services;
using WorkflowApp.Application.Tasks.Services;
using WorkflowApp.Application.Workforce.Services;

namespace WorkflowApp.Application;

/// <summary>
/// Registers the use-case services. These are scoped because they collaborate with the
/// per-request <c>IWorkflowDbContext</c> and <c>ICurrentUser</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Cross-cutting
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IActivityLogger, ActivityLogger>();
        services.AddScoped<INumberGenerator, NumberGenerator>();
        // Singleton: resolving the business time zone once is enough, and it never changes at runtime.
        services.AddSingleton<IBusinessCalendar, BusinessCalendar>();

        // Identity
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserAdminService, UserAdminService>();

        // Workforce
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IWorkforceQueryService, WorkforceQueryService>();
        services.AddScoped<IShiftMaintenanceService, ShiftMaintenanceService>();

        // Requests
        services.AddScoped<IRequestService, RequestService>();
        services.AddScoped<IRequestTriageService, RequestTriageService>();
        services.AddScoped<IAttachmentService, AttachmentService>();

        // Tasks
        services.AddScoped<ITaskCreationService, TaskCreationService>();
        services.AddScoped<ITaskQueryService, TaskQueryService>();
        services.AddScoped<ITaskWorkflowService, TaskWorkflowService>();
        services.AddScoped<ITaskAssignmentService, TaskAssignmentService>();
        services.AddScoped<IWorkSessionService, WorkSessionService>();

        return services;
    }
}
