using System.Text.Json.Nodes;
using Anthropic.SDK.Common;

namespace ChatbotApi.Tools;

public static class ToolDefinitions
{
    public static List<Tool> GetAllTools()
    {
        return new List<Tool>
        {
            CreateTool(
                "get_all_employees",
                "Retrieves a list of all employees in the payroll system. Returns employee IDs, names, email addresses, department, position, pay type (Hourly=1, Salary=2), and pay rate. Use this to find employees or get an overview of the workforce.",
                new JsonObject()
            ),
            CreateTool(
                "get_employee_by_id",
                "Retrieves detailed information for a specific employee by their unique ID (GUID format). Returns the employee's full profile including name, email, department, position, pay type, and pay rate.",
                new JsonObject
                {
                    ["employeeId"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "The unique identifier (GUID) of the employee"
                    }
                },
                "employeeId"
            ),
            CreateTool(
                "get_time_entries",
                "Retrieves all time entries (clock-in/clock-out records) for a specific employee. Returns timestamps for when the employee clocked in and out. Use this to check work hours or attendance.",
                new JsonObject
                {
                    ["employeeId"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "The unique identifier (GUID) of the employee"
                    }
                },
                "employeeId"
            ),
            CreateTool(
                "get_tax_information",
                "Retrieves tax information for a specific employee. Returns federal and state tax filing status, withholding allowances, and any additional withholding amounts.",
                new JsonObject
                {
                    ["employeeId"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "The unique identifier (GUID) of the employee"
                    }
                },
                "employeeId"
            ),
            CreateTool(
                "get_deductions",
                "Retrieves all payroll deductions for a specific employee. Returns deduction types (Health=1, Dental=2, Vision=3, Retirement401k=4, LifeInsurance=5, Other=99) and their amounts.",
                new JsonObject
                {
                    ["employeeId"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "The unique identifier (GUID) of the employee"
                    }
                },
                "employeeId"
            ),
            CreateTool(
                "get_ewa_balance",
                "Calculates the Earned Wage Access (EWA) balance and available withdrawal for an employee. Returns: gross earned wages for the current pay period, estimated taxes, estimated deductions, net earned wages (the employee's available balance after taxes and deductions), and the amount available to withdraw today. Withdrawal rules: max 1 withdrawal per day, capped at $200 or 70% of net earned wages (whichever is lower). Always use this tool when a user asks about their balance, available funds, how much they can withdraw, or anything related to earned wage access.",
                new JsonObject
                {
                    ["employeeId"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "The unique identifier (GUID) of the employee"
                    }
                },
                "employeeId"
            )
        };
    }

    private static Tool CreateTool(string name, string description, JsonObject properties, params string[] required)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(required.Select(r => JsonValue.Create(r)).ToArray())
        };

        var function = new Function(name, description, schema);
        return new Tool(function);
    }
}
