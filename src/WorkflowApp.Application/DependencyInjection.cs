using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkflowApp.Application.Admin.Services;
using WorkflowApp.Application.Common.Events;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Identity.Services;
using WorkflowApp.Application.Notifications;
using WorkflowApp.Application.Reporting;
using WorkflowApp.Application.Requests.Services;
using WorkflowApp.Application.Tasks.Services;
using WorkflowApp.Application.Verifications.Services;
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
        // Scoped: one queue per unit of work, drained after that unit commits.
        services.AddScoped<IIntegrationEventQueue, IntegrationEventQueue>();
        // A no-op by default. The API host registers the SignalR publisher after this, and the
        // later registration is the one resolved — so a host without SignalR still works.
        services.TryAddSingleton<IIntegrationEventPublisher, NullIntegrationEventPublisher>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IActivityLogger, ActivityLogger>();
        services.AddScoped<INumberGenerator, NumberGenerator>();
        services.AddScoped<ILookupService, LookupService>();
        // Singleton: resolving the business time zone once is enough, and it never changes at runtime.
        services.AddSingleton<IBusinessCalendar, BusinessCalendar>();

        // Identity
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<ISetupService, SetupService>();

        // Workforce
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IWorkforceQueryService, WorkforceQueryService>();
        services.AddScoped<IShiftMaintenanceService, ShiftMaintenanceService>();

        // Requests
        services.AddScoped<IRequestService, RequestService>();
        services.AddScoped<IRequestTriageService, RequestTriageService>();
        services.AddScoped<IRequestBatchService, RequestBatchService>();
        services.AddScoped<IAttachmentService, AttachmentService>();

        // Tasks
        services.AddScoped<ITaskCreationService, TaskCreationService>();
        services.AddScoped<ITaskQueryService, TaskQueryService>();
        services.AddScoped<ITaskWorkflowService, TaskWorkflowService>();
        services.AddScoped<ITaskAssignmentService, TaskAssignmentService>();
        services.AddScoped<IWorkSessionService, WorkSessionService>();
        services.AddScoped<IQuickWorkService, QuickWorkService>();
        services.AddScoped<IQCService, QCService>();
        services.AddScoped<IClosureService, ClosureService>();
        services.AddScoped<ITaskCommentService, TaskCommentService>();
        services.AddScoped<ITaskDependencyService, TaskDependencyService>();
        services.AddScoped<IScopeChangeService, ScopeChangeService>();

        // Verifications — assigned investigation. Deliberately separate from both the task
        // services and QC: see VerificationService.
        services.AddScoped<IVerificationService, VerificationService>();

        // Notifications & audit
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();

        // Dashboards & reports
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IReportService, ReportService>();

        return services;
    }
}
