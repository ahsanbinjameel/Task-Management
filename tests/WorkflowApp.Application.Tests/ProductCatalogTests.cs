using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Admin.Dtos;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Common;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// PRODUCT-CORE §5: the ERP context is two orthogonal axes, not one tree.
///
/// <code>Request = Client? × ProductLocation(Module → Form → Surface)</code>
///
/// The catalog describes your product; each client runs an instance of it. Hanging forms off
/// clients would give every client a private copy of the same form and make the questions worth
/// asking unanswerable — "every Delivery Order detail-report issue across all clients", "which
/// forms generate the most support", "is this posting bug unique to ABC or are four clients
/// seeing it". These tests pin that the two axes stay independent.
/// </summary>
public class ProductCatalogTests
{
    private static async Task<TestHarness> CatalogAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();
        var admin = await h.CreateUserAsync("admin2");
        h.ActingAsAdmin(admin.Id);
        return h;
    }

    [Fact]
    public async Task A_form_belongs_to_a_module_and_a_surface_to_a_form()
    {
        using var h = await CatalogAsync();

        var sales = await h.Setup.CreateModuleAsync(new SaveModuleDto { Name = "Sales" });
        var order = await h.Setup.CreateFormAsync(
            new SaveFormDto { Name = "Delivery Order", ModuleId = sales.Value!.Id });
        var detail = await h.Setup.CreateFormSurfaceAsync(
            new SaveFormSurfaceDto { Name = "Detail Report", FormId = order.Value!.Id });

        Assert.True(detail.IsSuccess);
        Assert.Equal("Delivery Order", detail.Value!.FormName);
        Assert.Equal("Sales", detail.Value.ModuleName);
    }

    /// <summary>
    /// The heart of §5. Two clients reporting the same problem point at the <em>same</em> catalog
    /// row, which is what makes "is this unique to ABC?" a query rather than a guess.
    /// </summary>
    [Fact]
    public async Task Two_clients_reporting_the_same_form_share_one_catalog_row()
    {
        using var h = await CatalogAsync();

        var sales = await h.Setup.CreateModuleAsync(new SaveModuleDto { Name = "Sales" });
        var order = await h.Setup.CreateFormAsync(
            new SaveFormDto { Name = "Delivery Order", ModuleId = sales.Value!.Id });

        var requester = await h.CreateUserAsync("faisal");
        var reviewer = await h.CreateUserAsync("ahsan");
        h.ActingAsAdmin(reviewer.Id);

        foreach (var client in new[] { "Impression Sourcing", "ABC Ltd" })
        {
            var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
            {
                Title = $"Detail report total wrong at {client}",
                Description = "The total row does not match the lines.",
                Type = RequestType.Bug,
                ClientName = client
            });

            await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
            {
                Outcome = TriageOutcome.Approve,
                ApprovedPriority = Priority.Normal,
                ModuleId = sales.Value.Id,
                FormId = order.Value!.Id
            });
        }

        var against = await h.Db.Requests.AsNoTracking()
            .Where(r => r.FormId == order.Value!.Id)
            .Select(r => r.ClientId)
            .ToListAsync();

        Assert.Equal(2, against.Count);
        Assert.Equal(2, against.Distinct().Count());
    }

    /// <summary>
    /// Internal work is the client axis left empty. There is no "Internal" client and there must
    /// never be one — inventing a fake row to satisfy a foreign key would put made-up data into
    /// every client report.
    /// </summary>
    [Fact]
    public async Task Internal_work_is_the_client_axis_left_empty()
    {
        using var h = await CatalogAsync();

        var accounts = await h.Setup.CreateModuleAsync(new SaveModuleDto { Name = "Accounts" });
        var posting = await h.Setup.CreateFormAsync(
            new SaveFormDto { Name = "Accounts Posting", ModuleId = accounts.Value!.Id });

        var requester = await h.CreateUserAsync("internal");
        var reviewer = await h.CreateUserAsync("ahsan");
        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Posting screen needs a keyboard shortcut",
            Description = "Internal improvement, no client behind it.",
            Type = RequestType.ChangeRequest
        });

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.Low,
            ModuleId = accounts.Value.Id,
            FormId = posting.Value!.Id
        });

        var task = await h.Db.Tasks.AsNoTracking().SingleAsync();

        Assert.Null(task.ClientId);
        Assert.Equal(accounts.Value.Id, task.ModuleId);
        Assert.Equal(posting.Value.Id, task.FormId);
    }

    [Fact]
    public async Task The_task_inherits_the_product_location_from_its_request()
    {
        using var h = await CatalogAsync();

        var sales = await h.Setup.CreateModuleAsync(new SaveModuleDto { Name = "Sales" });
        var order = await h.Setup.CreateFormAsync(
            new SaveFormDto { Name = "Delivery Order", ModuleId = sales.Value!.Id });
        var detail = await h.Setup.CreateFormSurfaceAsync(
            new SaveFormSurfaceDto { Name = "Detail Report", FormId = order.Value!.Id });

        var requester = await h.CreateUserAsync("faisal");
        var reviewer = await h.CreateUserAsync("ahsan");
        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Detail report total is wrong",
            Description = "Mismatch on the total row.",
            Type = RequestType.Bug
        });

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.Normal,
            ModuleId = sales.Value.Id,
            FormId = order.Value!.Id,
            FormSurfaceId = detail.Value!.Id
        });

        var task = await h.Db.Tasks.AsNoTracking().SingleAsync();

        Assert.Equal(sales.Value.Id, task.ModuleId);
        Assert.Equal(order.Value.Id, task.FormId);
        Assert.Equal(detail.Value.Id, task.FormSurfaceId);
    }

    /// <summary>
    /// Moving a request up the catalog has to drop what sat below it. Sales → Accounts while still
    /// pointing at the Delivery Order form names a combination that does not exist, and a report
    /// grouped by module would count it under Accounts while naming a Sales form.
    /// </summary>
    [Fact]
    public async Task Changing_the_module_clears_the_form_below_it()
    {
        using var h = await CatalogAsync();

        var sales = await h.Setup.CreateModuleAsync(new SaveModuleDto { Name = "Sales" });
        var accounts = await h.Setup.CreateModuleAsync(new SaveModuleDto { Name = "Accounts" });
        var order = await h.Setup.CreateFormAsync(
            new SaveFormDto { Name = "Delivery Order", ModuleId = sales.Value!.Id });
        var detail = await h.Setup.CreateFormSurfaceAsync(
            new SaveFormSurfaceDto { Name = "Detail Report", FormId = order.Value!.Id });

        var requester = await h.CreateUserAsync("faisal");
        var reviewer = await h.CreateUserAsync("ahsan");
        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Misfiled at first",
            Description = "Reviewer places it, then moves it.",
            Type = RequestType.Bug
        });

        var stored = await h.Db.Requests.SingleAsync(r => r.Id == request.Value!.Id);
        stored.ModuleId = sales.Value.Id;
        stored.FormId = order.Value.Id;
        stored.FormSurfaceId = detail.Value!.Id;
        await h.Db.SaveChangesAsync();

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.Normal,
            ModuleId = accounts.Value!.Id
        });

        var task = await h.Db.Tasks.AsNoTracking().SingleAsync();

        Assert.Equal(accounts.Value.Id, task.ModuleId);
        Assert.Null(task.FormId);
        Assert.Null(task.FormSurfaceId);
    }

    [Fact]
    public async Task Two_modules_may_each_have_a_form_of_the_same_name()
    {
        using var h = await CatalogAsync();

        var sales = await h.Setup.CreateModuleAsync(new SaveModuleDto { Name = "Sales" });
        var stock = await h.Setup.CreateModuleAsync(new SaveModuleDto { Name = "Inventory" });

        var first = await h.Setup.CreateFormAsync(
            new SaveFormDto { Name = "Adjustment", ModuleId = sales.Value!.Id });
        var second = await h.Setup.CreateFormAsync(
            new SaveFormDto { Name = "Adjustment", ModuleId = stock.Value!.Id });

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        // But not twice in the same module — that really is the same form typed twice.
        var duplicate = await h.Setup.CreateFormAsync(
            new SaveFormDto { Name = "adjustment", ModuleId = sales.Value.Id });

        Assert.False(duplicate.IsSuccess);
    }

    [Fact]
    public async Task Catalog_rows_are_retired_rather_than_deleted()
    {
        using var h = await CatalogAsync();

        var sales = await h.Setup.CreateModuleAsync(new SaveModuleDto { Name = "Sales" });
        var order = await h.Setup.CreateFormAsync(
            new SaveFormDto { Name = "Delivery Order", ModuleId = sales.Value!.Id });

        var retired = await h.Setup.SetFormActiveAsync(order.Value!.Id, false);

        Assert.True(retired.IsSuccess);
        Assert.False(retired.Value!.IsActive);

        // Still there, still answering for the requests filed against it.
        Assert.True(await h.Db.Forms.AnyAsync(f => f.Id == order.Value!.Id));

        // And out of the picker, which is the whole point of retiring it.
        var offered = await h.Lookups.FormsAsync(sales.Value.Id, null);
        Assert.Empty(offered);
    }

    [Fact]
    public async Task The_form_picker_narrows_by_module_and_takes_no_client()
    {
        using var h = await CatalogAsync();

        var sales = await h.Setup.CreateModuleAsync(new SaveModuleDto { Name = "Sales" });
        var accounts = await h.Setup.CreateModuleAsync(new SaveModuleDto { Name = "Accounts" });

        await h.Setup.CreateFormAsync(new SaveFormDto { Name = "Delivery Order", ModuleId = sales.Value!.Id });
        await h.Setup.CreateFormAsync(new SaveFormDto { Name = "Sales Invoice", ModuleId = sales.Value.Id });
        await h.Setup.CreateFormAsync(new SaveFormDto { Name = "Posting", ModuleId = accounts.Value!.Id });

        var salesForms = await h.Lookups.FormsAsync(sales.Value.Id, null);

        Assert.Equal(2, salesForms.Count);
        Assert.All(salesForms, f => Assert.Equal("Sales", f.ModuleName));
    }

    [Fact]
    public void The_product_location_reads_as_one_line_and_tolerates_gaps()
    {
        Assert.Equal(
            "Sales · Delivery Order · Detail Report",
            ProductLocation.Format("Sales", "Delivery Order", "Detail Report"));

        // Placed only as far as anyone knew: still a useful line, not "Sales ·  · ".
        Assert.Equal("Sales", ProductLocation.Format("Sales", null, null));

        // Nothing placed at all, so a screen can leave the line out rather than print a label.
        Assert.Null(ProductLocation.Format(null, null, null));
    }
}
