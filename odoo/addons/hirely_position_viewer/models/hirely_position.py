from odoo import api, fields, models
from odoo.exceptions import UserError

DATA_TYPES = [
    ("String", "String"),
    ("Text", "Text"),
    ("Image", "Image"),
    ("Numeric", "Numeric"),
    ("Date", "Date"),
    ("Period", "Period"),
    ("Boolean", "Boolean"),
    ("Dropdown", "Dropdown"),
]

DATA_TYPE_KEYS = {key for key, _ in DATA_TYPES}

WRITE_GUARD_MESSAGE = (
    "Imported positions are read-only. Use the 'Import from Hirely Desk' "
    "action to refresh them."
)


class HirelyReadonlyMixin(models.AbstractModel):
    """Rejects writes from the user interface; only the import wizard may write."""

    _name = "hirely.readonly.mixin"
    _description = "Hirely Desk read-only guard"

    def create(self, vals_list):
        self._check_writable()
        return super().create(vals_list)

    def write(self, vals):
        self._check_writable()
        return super().write(vals)

    def unlink(self):
        self._check_writable()
        return super().unlink()

    def _check_writable(self):
        if not self.env.context.get("hirely_import"):
            raise UserError(WRITE_GUARD_MESSAGE)


class HirelyPosition(models.Model):
    """A position imported from Hirely Desk together with aggregated results."""

    _name = "hirely.position"
    _description = "Hirely Desk Position"
    _order = "name, id"
    _rec_name = "name"
    _inherit = ["hirely.readonly.mixin"]

    name = fields.Char(string="Title", required=True, readonly=True, index=True)
    source_position_id = fields.Char(
        string="Source ID", required=True, readonly=True, index=True,
        help="Identifier of the position in the Hirely Desk platform.",
    )
    company = fields.Char(string="Company", readonly=True)
    level = fields.Char(string="Level", readonly=True)
    short_description = fields.Text(string="Description", readonly=True)
    cv_count = fields.Integer(string="CVs", readonly=True)
    imported_at = fields.Datetime(string="First imported", readonly=True)
    last_imported_at = fields.Datetime(string="Last imported", readonly=True)
    attribute_ids = fields.One2many(
        "hirely.position.attribute", "position_id",
        string="Attributes", readonly=True,
    )
    attribute_count = fields.Integer(
        string="Attribute count", compute="_compute_attribute_count",
    )

    _sql_constraints = [
        (
            "source_position_id_uniq",
            "unique(source_position_id)",
            "This Hirely Desk position has already been imported.",
        ),
    ]

    @api.depends("attribute_ids")
    def _compute_attribute_count(self):
        for position in self:
            position.attribute_count = len(position.attribute_ids)

    @api.model
    def _upsert_from_payload(self, payload):
        """Create or update one position (keyed by source_position_id)."""
        source_id = str(payload.get("positionId") or "").strip()
        title = (payload.get("title") or "").strip()
        if not source_id or not title:
            raise UserError("The API response is missing the position id or title.")

        position = self.sudo().with_context(hirely_import=True).search(
            [("source_position_id", "=", source_id)], limit=1
        )
        now = fields.Datetime.now()
        values = {
            "name": title,
            "company": payload.get("company"),
            "level": payload.get("level"),
            "short_description": payload.get("shortDescription") or "",
            "cv_count": int(payload.get("cvCount") or 0),
            "last_imported_at": now,
        }

        attributes = [
            self.env["hirely.position.attribute"]
            .sudo()
            .with_context(hirely_import=True)
            ._prepare_from_payload(index, attribute)
            for index, attribute in enumerate(payload.get("attributes") or [])
        ]

        if position:
            position.write(values)
            position.attribute_ids.with_context(hirely_import=True).unlink()
            self.env["hirely.position.attribute"].sudo().with_context(hirely_import=True).create(
                [dict(attribute, position_id=position.id) for attribute in attributes]
            )
            return position

        values.update({"source_position_id": source_id, "imported_at": now})
        position = (
            self.sudo()
            .with_context(hirely_import=True)
            .create([dict(values, attribute_ids=[(0, 0, attribute) for attribute in attributes])])
        )
        return position


class HirelyPositionAttribute(models.Model):
    """One attribute of an imported position with its type-specific aggregate."""

    _name = "hirely.position.attribute"
    _description = "Hirely Desk Position Attribute"
    _order = "position_id, sequence, id"
    _inherit = ["hirely.readonly.mixin"]

    position_id = fields.Many2one(
        "hirely.position", string="Position", required=True,
        ondelete="cascade", readonly=True, index=True,
    )
    name = fields.Char(string="Attribute", required=True, readonly=True)
    category = fields.Char(string="Category", readonly=True)
    data_type = fields.Selection(DATA_TYPES, string="Type", readonly=True)
    sequence = fields.Integer(string="Order", readonly=True)
    filled_count = fields.Integer(string="Reported", readonly=True)

    numeric_count = fields.Integer(string="Count", readonly=True)
    numeric_average = fields.Float(string="Average", readonly=True, digits=(16, 2))
    numeric_min = fields.Float(string="Minimum", readonly=True, digits=(16, 2))
    numeric_max = fields.Float(string="Maximum", readonly=True, digits=(16, 2))

    boolean_true_count = fields.Integer(string="True", readonly=True)
    boolean_false_count = fields.Integer(string="False", readonly=True)

    date_earliest = fields.Date(string="Earliest", readonly=True)
    date_latest = fields.Date(string="Latest", readonly=True)

    period_earliest_start = fields.Date(string="Earliest start", readonly=True)
    period_latest_end = fields.Date(string="Latest end", readonly=True)

    distinct_value_count = fields.Integer(string="Distinct values", readonly=True)
    value_ids = fields.One2many(
        "hirely.position.attribute.value", "attribute_id",
        string="Most popular values", readonly=True,
    )

    _sql_constraints = [
        (
            "position_name_uniq",
            "unique(position_id, name)",
            "An attribute may appear only once per position.",
        ),
    ]

    @api.model
    def _prepare_from_payload(self, sequence, payload):
        """Flatten one API attribute payload into an Odoo values dictionary."""
        data_type = payload.get("dataType")
        values = {
            "name": (payload.get("name") or "").strip() or "Attribute",
            "category": payload.get("category"),
            "data_type": data_type if data_type in DATA_TYPE_KEYS else False,
            "sequence": sequence,
            "filled_count": int(payload.get("filledCount") or 0),
            "value_ids": [],
        }

        numeric = payload.get("numeric")
        if numeric:
            values.update(
                {
                    "numeric_count": int(numeric.get("count") or 0),
                    "numeric_average": _as_float(numeric.get("average")),
                    "numeric_min": _as_float(numeric.get("min")),
                    "numeric_max": _as_float(numeric.get("max")),
                }
            )

        boolean = payload.get("boolean")
        if boolean:
            values.update(
                {
                    "boolean_true_count": int(boolean.get("trueCount") or 0),
                    "boolean_false_count": int(boolean.get("falseCount") or 0),
                }
            )

        date = payload.get("date")
        if date:
            values.update(
                {
                    "date_earliest": date.get("earliest"),
                    "date_latest": date.get("latest"),
                }
            )

        period = payload.get("period")
        if period:
            values.update(
                {
                    "period_earliest_start": period.get("earliestStart"),
                    "period_latest_end": period.get("latestEnd"),
                }
            )

        top_values = payload.get("topValues") or []
        values["distinct_value_count"] = int(payload.get("distinctValueCount") or 0)
        values["value_ids"] = [
            (0, 0, {"rank": rank, "value": str(entry.get("value") or ""),
                    "occurrence_count": int(entry.get("count") or 0)})
            for rank, entry in enumerate(top_values)
        ]
        return values


class HirelyPositionAttributeValue(models.Model):
    """A most-popular value for a text-like attribute."""

    _name = "hirely.position.attribute.value"
    _description = "Hirely Desk Position Attribute Value"
    _order = "attribute_id, rank, id"
    _inherit = ["hirely.readonly.mixin"]

    attribute_id = fields.Many2one(
        "hirely.position.attribute", string="Attribute", required=True,
        ondelete="cascade", readonly=True, index=True,
    )
    rank = fields.Integer(string="Rank", readonly=True)
    value = fields.Char(string="Value", required=True, readonly=True)
    occurrence_count = fields.Integer(string="Candidates", readonly=True)


def _as_float(value):
    return float(value) if value is not None else 0.0
