﻿using System.ComponentModel.DataAnnotations;

namespace Host.UI.Login
{
    public class RecoverInputModel
    {
        [Required]
        public string Email { get; set; }

        public string ReturnUrl { get; set; }
    }
}