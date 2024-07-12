module Bank.UserSession.Middleware

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder

open Bank.Employee.Domain
open Lib.SharedTypes

let getSession (context: HttpContext) =
   let employeeId =
      context.Session.GetString("EmployeeId") |> Guid.parseOptional

   let orgId = context.Session.GetString("OrgId") |> Guid.parseOptional

   let email =
      context.Session.GetString("Email") |> Email.ofString "User session email"

   let role = context.Session.GetString("Role") |> Role.fromString

   match employeeId, orgId, email, role with
   | Some employeeId, Some orgId, Ok email, Some role ->
      let session: UserSession = {
         EmployeeId = EmployeeId employeeId
         OrgId = OrgId orgId
         FirstName = context.Session.GetString("FirstName")
         LastName = context.Session.GetString("LastName")
         Email = email
         Role = role
      }

      Some session
   | _ -> None

[<AllowNullLiteral>]
type private RequiredRoleMetadata(access: Permissions.Access) =
   member x.Access = access

type RouteHandlerBuilder with
   member x.RBAC(roles: Permissions.Access) =
      x.WithMetadata(RequiredRoleMetadata(roles)) |> ignore
      x

type RoleMiddleware(next: RequestDelegate) =
   member x.InvokeAsync(context: HttpContext) = task {
      let session = getSession context
      let endpoint = context.GetEndpoint()

      if obj.ReferenceEquals(endpoint, null) then
         do! next.Invoke(context)
      else
         let requiredRole =
            endpoint.Metadata.GetMetadata<RequiredRoleMetadata>()

         let access =
            if isNull requiredRole then
               None
            else
               Some requiredRole.Access

         match access, session with
         | Some access, Some userSession ->
            if access.HasAccess(userSession.Role) then
               do! next.Invoke(context)
            else
               context.Response.StatusCode <- StatusCodes.Status403Forbidden
               do! context.Response.WriteAsync("Forbidden")
         | _ -> do! next.Invoke(context)
   }
