package net.aliasvault.app.autofill.utils

import android.service.autofill.Dataset
import android.view.autofill.AutofillId
import android.view.autofill.AutofillValue
import net.aliasvault.app.autofill.models.FieldType
import net.aliasvault.app.vaultstore.models.Item

/**
 * Maps a vault [Item] onto the detected autofill fields, setting values on a
 * `Dataset.Builder` and reporting which value was used as the picker label
 * suffix.
 */
object AutofillFieldMapper {
    /**
     * Result of applying an item to a dataset builder.
     *
     * @property hasValue True if at least one field received a value.
     * @property labelSuffix First non-password value that was set (email or
     *   username), suitable for appending to the picker row label, or null if
     *   only the password field was set.
     */
    data class ApplyResult(val hasValue: Boolean, val labelSuffix: String?)

    /**
     * Set autofill values on [builder] for each of [fields] using the
     * corresponding data from [item]. Empty values are skipped.
     */
    fun applyItem(
        builder: Dataset.Builder,
        item: Item,
        fields: List<Pair<AutofillId, FieldType>>,
    ): ApplyResult {
        var hasValue = false
        var labelSuffix: String? = null

        for ((autofillId, fieldType) in fields) {
            val value = pickValue(item, fieldType) ?: continue
            builder.setValue(autofillId, AutofillValue.forText(value))
            hasValue = true
            if (labelSuffix == null && fieldType != FieldType.PASSWORD) {
                labelSuffix = value
            }
        }

        return ApplyResult(hasValue, labelSuffix)
    }

    private fun pickValue(item: Item, fieldType: FieldType): String? = when (fieldType) {
        FieldType.PASSWORD -> item.password.takeUnless { it.isNullOrEmpty() }
        FieldType.EMAIL ->
            item.email.takeUnless { it.isNullOrEmpty() }
                ?: item.username.takeUnless { it.isNullOrEmpty() }
        FieldType.USERNAME ->
            item.username.takeUnless { it.isNullOrEmpty() }
                ?: item.email.takeUnless { it.isNullOrEmpty() }
        FieldType.UNKNOWN ->
            item.email.takeUnless { it.isNullOrEmpty() }
                ?: item.username.takeUnless { it.isNullOrEmpty() }
    }
}
