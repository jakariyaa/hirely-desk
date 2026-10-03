import logging

import requests

from odoo import api, fields, models
from odoo.exceptions import UserError

_logger = logging.getLogger(__name__)

API_PATH = "/api/v1/positions/summary"
CONFIG_PARAMETER = "hirely_position_viewer.api_url"
DEFAULT_API_URL = "http://host.docker.internal:5191"
REQUEST_TIMEOUT_SECONDS = 15


class HirelyImportWizard(models.TransientModel):
    """Import one position's aggregated results from Hirely Desk by API token."""

    _name = "hirely.import.wizard"
    _description = "Import from Hirely Desk"

    api_url = fields.Char(
        string="Hirely Desk URL", required=True,
        default=lambda self: self._default_api_url(),
        help="Base URL of the Hirely Desk application.",
    )
    api_token = fields.Char(
        string="API token", required=True,
        help="Per-position token generated on the position form in Hirely Desk.",
    )

    @api.model
    def _default_api_url(self):
        return (
            self.env["ir.config_parameter"].sudo().get_param(CONFIG_PARAMETER)
            or DEFAULT_API_URL
        ).rstrip("/")

    def action_import(self):
        self.ensure_one()
        url = (self.api_url or "").strip().rstrip("/")
        token = (self.api_token or "").strip()
        if not url or not token:
            raise UserError("Enter the Hirely Desk URL and the API token.")

        payload = self._fetch_summary(url, token)
        position = self.env["hirely.position"]._upsert_from_payload(payload)
        _logger.info(
            "Imported Hirely Desk position %s (%s)", position.id, position.name
        )
        return {
            "type": "ir.actions.act_window",
            "name": position.name,
            "res_model": "hirely.position",
            "view_mode": "form",
            "res_id": position.id,
            "target": "current",
        }

    @api.model
    def _fetch_summary(self, url, token):
        endpoint = url + API_PATH
        try:
            response = requests.get(
                endpoint,
                headers={"Authorization": f"Bearer {token}"},
                timeout=REQUEST_TIMEOUT_SECONDS,
            )
        except requests.exceptions.Timeout:
            raise UserError(
                "The Hirely Desk API did not respond in time. "
                "Check the URL and try again."
            )
        except requests.exceptions.RequestException as error:
            _logger.warning("Hirely Desk request failed: %s", error)
            raise UserError(
                f"Could not reach the Hirely Desk API at {endpoint}. "
                "Check the URL and that the application is running."
            )

        if response.status_code == 401:
            raise UserError(
                "The API token was rejected. It may be invalid or revoked; "
                "generate a new token on the position form in Hirely Desk."
            )
        if response.status_code == 403:
            raise UserError("This API token is not allowed to read the position.")
        if response.status_code == 404:
            raise UserError(
                "The position was not found. It may have been deleted in Hirely Desk."
            )
        if response.status_code == 429:
            raise UserError("The Hirely Desk API rate limit was reached. Try again later.")
        if response.status_code != 200:
            raise UserError(
                f"The Hirely Desk API returned HTTP {response.status_code}."
            )

        try:
            payload = response.json()
        except ValueError:
            raise UserError("The Hirely Desk API returned an unreadable response.")

        if not isinstance(payload, dict) or not payload.get("positionId"):
            raise UserError("The Hirely Desk API response is missing the position data.")
        return payload
