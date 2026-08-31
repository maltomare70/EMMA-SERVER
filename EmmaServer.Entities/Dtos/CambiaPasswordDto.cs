using System;
using System.Collections.Generic;
using System.Text;

namespace EmmaServer.Entities.Dtos;

public class CambiaPasswordRequest
{
    public string email { get; init; } = string.Empty;
    public string oldPassword { get; init; } = string.Empty;
    public string newPassword { get; init; } = string.Empty;
    public string hash { get; set; } = string.Empty;
}
