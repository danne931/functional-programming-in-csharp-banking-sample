'use strict'

const state = {
  accounts: {},
  selectedAccountId: null,
  transferRecipients: {}
}

const connection = new signalR.HubConnectionBuilder()
  .withUrl('/accountHub')
  .build()

connection.on('ReceiveError', function (err) {
  const msg = `Error: ${err}`
  notifyError(msg)
})

connection.on('ReceiveMessage', function ({ newState, event }) {
  renderAccountState(newState)
  renderEventIntoListView(event)
  if (event.name === 'RegisteredInternalTransferRecipient') {
    interpolateTransferRecipientSelection(newState)
    state.transferRecipients = newState.transferRecipients
  }
})

connection
  .start()
  .then(() => fetch('/accounts'))
  .then(res => {
    if (!res.ok) {
      throw new Error(`Http status: ${res.status}`)
    }
    return res.json()
  })
  .then(accounts => {
    state.accounts = accounts
    renderAccountsList(accounts)
    return accountSelected(accounts[0].entityId)
  })
  .catch(notifyError)

function accountSelected (accountId) {
  return addAccountToConnectionGroup(accountId)
    .then(() => Promise.all([
      fetch(`/accounts/${accountId}`),
      fetch(`/diagnostic/events/${accountId}`)
    ]))
    .then(responses =>
      Promise.all(responses.map(res => {
        if (!res.ok) {
          throw new Error(`Http status: ${res.status}`)
        }
        return res.json()
      }))
    )
    .then(([account, events]) => {
      renderAccountState(account)
      interpolateTransferRecipientSelection(account)
      state.transferRecipients = account.transferRecipients
      renderEventsIntoListView(events.reverse())
    })
}

function addAccountToConnectionGroup (accountId) {
  const promise = state.selectedAccountId != null
    ? connection.invoke('RemoveFromConnectionGroup', state.selectedAccountId)
    : Promise.resolve()

  return promise
    .then(() => connection.invoke('AddToConnectionGroup', accountId))
    .then(() => {
      state.selectedAccountId = accountId
      highlightSelectedAccount(accountId)
    })
}

function renderAccountsList (accounts) {
  const accountsAsNodes = accounts.reduce((acc, account) => {
    const input = document.createElement('input')
    input.type = 'radio'
    input.name = 'selected-account'
    input.value = account.entityId
    input.id = account.entityId

    const label = document.createElement('label')
    label.setAttribute('for', account.entityId)
    label.textContent = `${account.firstName} ${account.lastName} - Account Id: ${account.entityId}`

    return acc.concat([input, label, document.createElement('br')])
  }, [])

  document
    .getElementById('accounts-list')
    .prepend(...accountsAsNodes)
}

function highlightSelectedAccount (accountId) {
  document
    .querySelector(`#accounts-list input[value=${accountId}]`)
    .setAttribute('checked', true)
}

function renderAccountState (account) {
  document.getElementById('account-balance').textContent =
    `$${account.balance}`
  document.getElementById('account-name').textContent =
    `${account.firstName} ${account.lastName}`
  document
    .getElementById('account-daily-debit-limit')
    .textContent = account.dailyDebitLimit !== -1
      ? account.dailyDebitLimit
      : 'No limit set'
  document
    .getElementById('account-daily-debit-accrued')
    .textContent = account.dailyDebitAccrued
  document.getElementById('account-debit-card-lock').checked =
    // TODO: Send string to client instead of enum
    account.status === 1
}

function interpolateTransferRecipientSelection (account) {
  const transferRecipientsAsNodes = Object.entries(account.transferRecipients)
    .map(([key, val]) => {
      const option = document.createElement('option')
      option.value = key
      option.label = `${val.firstName} ${val.lastName}`
      return option
    })
  document
    .querySelector('#account-transfer-form select')
    .replaceChildren(...transferRecipientsAsNodes)
}

function renderEventsIntoListView (events) {
  const eventsAsNodes = events.map(evt => {
    const li = document.createElement('li')
    li.textContent = eventToTransactionString(evt)
    return li
  })
  document
    .getElementById('messagesList')
    .replaceChildren(...eventsAsNodes)
}

function renderEventIntoListView (evt) {
  const li = document.createElement('li')
  li.textContent = eventToTransactionString(evt)
  document.getElementById('messagesList').prepend(li)
}

function eventToTransactionString (evt) {
  switch (evt.name) {
    case 'CreatedAccount':
      return `Created account for ${evt.firstName} ${evt.lastName} with balance $${evt.balance} on ${evt.timestamp}`
    case 'DepositedCash':
      return `$${evt.depositedAmount} deposited on ${evt.timestamp} by ${evt.origin}`
    case 'DebitedAccount':
      return `$${evt.debitedAmount} debited by ${evt.origin} on ${evt.timestamp}`
    case 'DailyDebitLimitUpdated':
      return `Daily debit limit updated to $${evt.debitLimit} on ${evt.timestamp}`
    case 'LockedCard':
      return `Debit card locked on ${evt.timestamp}`
    case 'UnlockedCard':
      return `Debit card unlocked on ${evt.timestamp}`
    case 'DebitedTransfer':
      return `$${evt.debitedAmount} transferred to ${evt.recipient.firstName} ${evt.recipient.lastName} on ${evt.timestamp}`
    case 'RegisteredInternalTransferRecipient':
      return `Registered transfer recipient ${evt.firstName} ${evt.lastName} on ${evt.timestamp}`
    default:
      return 'Unknown event'
  }
}

const accountsListFormEl = document.getElementById('accounts-list')
accountsListFormEl.addEventListener('submit', e => {
  e.preventDefault()
  const formData = new FormData(accountsListFormEl)
  accountSelected(formData.get('selected-account'))
})

function cardLockToggled () {
  const url = document.getElementById('account-debit-card-lock').checked
    ? '/accounts/lock'
    : '/accounts/unlock'
  jsonPost(url, { entityId: state.selectedAccountId })
}

listenForFormSubmit(
  document.getElementById('account-deposit-form'),
  '/accounts/deposit',
  formData => ({
    entityId: state.selectedAccountId,
    amount: formData.get('account-deposit-amount')
  })
)

listenForFormSubmit(
  document.getElementById('account-debit-form'),
  '/accounts/debit',
  formData => ({
    entityId: state.selectedAccountId,
    amount: formData.get('account-debit-amount'),
    origin: formData.get('account-debit-origin')
  })
)

listenForFormSubmit(
  document.getElementById('account-daily-debit-limit-form'),
  '/accounts/daily-debit-limit',
  formData => ({
    entityId: state.selectedAccountId,
    debitLimit: formData.get('account-daily-debit-limit')
  })
)

listenForFormSubmit(
  document.getElementById('account-transfer-form'),
  '/transfers',
  formData => ({
    entityId: state.selectedAccountId,
    amount: formData.get('account-transfer-amount'),
    recipient: getTransferRecipient(
      formData.get('account-transfer-recipient-id')
    )
  })
)

listenForFormSubmit(
  document.getElementById('register-transfer-recipient-form'),
  '/transfers/register-recipient',
  formData => ({
    entityId: state.selectedAccountId,
    recipient: {
      firstName: formData.get('transfer-recipient-first-name'),
      lastName: formData.get('transfer-recipient-last-name'),
      identification: formData.get('transfer-recipient-account-number'),
      accountEnvironment: parseInt(formData.get('transfer-recipient-account-environment')),
      identificationStrategy: 0
    }
  })
)

function listenForFormSubmit (formEl, url, formDataToProps) {
  formEl.addEventListener('submit', e => {
    e.preventDefault()
    jsonPost(url, formDataToProps(new FormData(formEl)))
      .then(_ => formEl.reset())
  })
}

function getTransferRecipient (recipientId) {
  return state.transferRecipients[recipientId]
}

function jsonPost (url, props) {
  return fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(props)
  })
  .catch(notifyError)
}

function notifyError (err) {
  const errMsg = err.toString()
  console.error(errMsg)
  alert(errMsg)
}
