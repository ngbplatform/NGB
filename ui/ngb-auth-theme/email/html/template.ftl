<#macro emailLayout>
<!DOCTYPE html>
<html>
<body style="margin:0;padding:0;background:#F5F7FA;font-family:system-ui,-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1F2933;">
  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#F5F7FA;padding:24px 0;">
    <tr>
      <td align="center">
        <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#FFFFFF;border:1px solid #CBD5E1;border-radius:4px;overflow:hidden;">
          <tr>
            <td align="center" style="padding:24px 24px 12px;">
              <img src="cid:ngb_logo.svg" alt="NGB" width="148" style="display:block;height:auto;border:0;" />
            </td>
          </tr>
          <tr>
            <td style="padding:0 24px 24px; font-size:14px; line-height:1.6; color:#1F2933;">
              <#nested>
            </td>
          </tr>
          <tr>
            <td style="border-top:1px solid #CBD5E1;padding:14px 24px;text-align:center;font-size:13px;color:#4B5563;">
              Secure account access
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
</#macro>
