using System.ComponentModel.DataAnnotations;

namespace NGB.Runtime.Ui;

public enum YesNo: short
{
    [Display(Name = "Yes")]
    True,
    
    [Display(Name = "No")]
    False
}
