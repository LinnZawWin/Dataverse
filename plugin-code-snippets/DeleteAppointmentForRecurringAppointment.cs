private const string DeleteActivityRoleName = "CS Delete Activity";
public void Execute(IServiceProvider serviceProvider)
{
    ITracingService tracer = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
    IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
    IOrganizationServiceFactory factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
    IOrganizationService systemService = factory.CreateOrganizationService(null);

    try
    {
        if (context.PrimaryEntityName.Equals(RecurringAppointmentMaster.EntityLogicalName, StringComparison.OrdinalIgnoreCase))
        {
            if (context.MessageName.Equals("DeleteOpenInstances", 
                StringComparison.OrdinalIgnoreCase) && context.Stage == 20) // Pre-operation
            {
                DeleteFutureAppointments(((Entity)context.InputParameters["Target"]).ToEntity<RecurringAppointmentMaster>().Id, 
                    (DateTime)context.InputParameters["SeriesEndDate"], 
                    systemService);
            }
            else if (context.MessageName.Equals("AddRecurrence", 
                StringComparison.OrdinalIgnoreCase) && context.Stage == 10) // Pre-validation
            {
                ElevateDeleteAppointmentPermission(context.InitiatingUserId, context.BusinessUnitId, 
                    (Guid)context.InputParameters["AppointmentId"], 
                    systemService);
            }
            else if (context.MessageName.Equals("AddRecurrence", 
                StringComparison.OrdinalIgnoreCase) && context.Stage == 40) // Post-operation
            {
                RemoveDeleteActivityRole(context.InitiatingUserId, 
                    systemService);
            }
        }
    }
    catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex)
    {
        tracer.Trace(ex.Detail.Message);
        throw;
    }
    catch (Exception ex)
    {
        tracer.Trace(ex.ToString());
        throw;
    }
}
private void DeleteFutureAppointments(Guid recurringAppointmentId, DateTime seriesEndDate, IOrganizationService systemService)
{
    QueryExpression query = new QueryExpression(Appointment.EntityLogicalName);
    query.ColumnSet = new ColumnSet("activityid");
    query.Criteria.AddCondition(new ConditionExpression("seriesid", ConditionOperator.Equal, recurringAppointmentId));
    query.Criteria.AddCondition(new ConditionExpression("scheduledstart", ConditionOperator.OnOrAfter, seriesEndDate));
    EntityCollection individualAppointments = systemService.RetrieveMultiple(query);
    foreach (Entity appt in individualAppointments.Entities)
    {
        systemService.Delete(appt.LogicalName, appt.Id);
    }
}
private void ElevateDeleteAppointmentPermission(Guid initiatingUserId, Guid businessUnitId, Guid appointmentId, IOrganizationService systemService)
{
    ShareDeleteRights(initiatingUserId, appointmentId, systemService);
    if (!DoesUserHaveDeletePrivilege(initiatingUserId, appointmentId, systemService))
    {
        AssignDeleteActivityRole(initiatingUserId, businessUnitId, systemService);
    }
}
private void ShareDeleteRights(Guid initiatingUserId, Guid appointmentId, IOrganizationService systemService)
{
    var grantAccessRequest = new GrantAccessRequest
    {
        PrincipalAccess = new PrincipalAccess
        {
            AccessMask = AccessRights.DeleteAccess,
            Principal = new EntityReference(SystemUser.EntityLogicalName, initiatingUserId)
        },
        Target = new EntityReference(Appointment.EntityLogicalName, appointmentId)
    };
    systemService.Execute(grantAccessRequest);
}
private void AssignDeleteActivityRole(Guid initiatingUserId, Guid businessUnitId, IOrganizationService systemService)
{
    QueryExpression query = new QueryExpression(Role.EntityLogicalName);
    query.ColumnSet = new ColumnSet("roleid");
    query.Criteria.AddCondition(new ConditionExpression("name", ConditionOperator.Equal, DeleteActivityRoleName));
    query.Criteria.AddCondition(new ConditionExpression("businessunitid", ConditionOperator.Equal, businessUnitId));
    EntityCollection roles = systemService.RetrieveMultiple(query);
    if (roles.Entities.Count > 0)
    {
        systemService.Associate(SystemUser.EntityLogicalName,
        initiatingUserId,
        new Relationship("systemuserroles_association"),
        new EntityReferenceCollection() { new EntityReference(Role.EntityLogicalName, roles[0].Id) });
    }
}
private bool DoesUserHaveDeletePrivilege(Guid initiatingUserId, Guid appointmentId, IOrganizationService systemService)
{
    RetrievePrincipalAccessRequest request = new RetrievePrincipalAccessRequest()
    {
        Principal = new EntityReference(SystemUser.EntityLogicalName, initiatingUserId),
        Target = new EntityReference(Appointment.EntityLogicalName, appointmentId)
    };
    RetrievePrincipalAccessResponse serviceResponse = (RetrievePrincipalAccessResponse)systemService.Execute(request);
    if (serviceResponse != null && serviceResponse.AccessRights.ToString() != String.Empty)
    {
        if ((serviceResponse.AccessRights & AccessRights.DeleteAccess) == AccessRights.DeleteAccess)
        {
            return true;
        }
    }
    return false;
}
private void RemoveDeleteActivityRole(Guid initiatingUserId, IOrganizationService systemService)
{
    Guid deleteActivityRoleId = RetrieveAssociatedDeleteActivityRoleId(initiatingUserId, systemService);
    if (deleteActivityRoleId != Guid.Empty)
    {
        systemService.Disassociate(SystemUser.EntityLogicalName,
            initiatingUserId,
            new Relationship("systemuserroles_association"),
            new EntityReferenceCollection() { new EntityReference(Role.EntityLogicalName, deleteActivityRoleId) });
    }
}
private Guid RetrieveAssociatedDeleteActivityRoleId(Guid initiatingUserId, IOrganizationService systemService)
{
    QueryExpression query = new QueryExpression(Role.EntityLogicalName);
    query.ColumnSet.AddColumns("roleid");
    query.Criteria.AddCondition("name", ConditionOperator.Equal, DeleteActivityRoleName);
    LinkEntity query_systemuserroles = query.AddLink("systemuserroles", "roleid", "roleid");
    query_systemuserroles.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, initiatingUserId);
    EntityCollection roles = systemService.RetrieveMultiple(query);
    if (roles.Entities.Count > 0)
    {
        return roles[0].Id;
    }
    return Guid.Empty;
}